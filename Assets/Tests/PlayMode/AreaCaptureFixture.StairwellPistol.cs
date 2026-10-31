using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused third-puzzle pistol reveal, shared pickup, live firearm input and story warehouse transfer.")]
        [PrebuildSetup(typeof(StairwellAuthoredAssetsSetup))]
        public IEnumerator StairwellThirdPuzzlePistol()
        {
            StairwellInteriorRoot stairwell = null;
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            float previousDelta = Time.captureDeltaTime;
            bool previousFocus = GameInput.MovementFocused;
            try
            {
                GameSessionState.BeginNewGame();
                GameSessionState.CommitHomeSleepAppearance();
                GameSessionState.PrepareStairwellArrival(StairwellArrivalKind.ApartmentDoor);
                // Earlier rooms are setup. Every third-room hand action, item transaction and exit is live.
                var state = GameSessionState.StairwellLabyrinthPuzzles;
                foreach (var action in new[] { StairwellLabyrinthPuzzleAction.Roll0, StairwellLabyrinthPuzzleAction.Roll0,
                    StairwellLabyrinthPuzzleAction.Roll1, StairwellLabyrinthPuzzleAction.Roll2,
                    StairwellLabyrinthPuzzleAction.Roll2, StairwellLabyrinthPuzzleAction.ClothBolt,
                    StairwellLabyrinthPuzzleAction.ShutterStop, StairwellLabyrinthPuzzleAction.CarriageSend })
                    Assert.That(state.TryApply(action), Is.True);
                Assert.That(GameSessionState.TryCollectWorldItem(BarPromenade.StairwellLabyrinthPuzzles.CrowbarSourceId,
                    InventoryItemId.CombatCrowbar), Is.True);
                Assert.That(GameSessionState.TryCollectWorldItem(BarPromenade.StairwellLabyrinthPuzzles.FlashlightSourceId,
                    InventoryItemId.ChestFlashlight), Is.True);
                Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.ChestFlashlight, true), Is.True);
                yield return SceneManager.LoadSceneAsync(SceneIds.StairwellInterior, LoadSceneMode.Single);
                stairwell = Object.FindAnyObjectByType<StairwellInteriorRoot>();
                yield return WaitForCorruptedStairwell(() => stairwell != null && stairwell.IsInitialized,
                    "The third-puzzle pistol scene did not initialize.");
                Time.captureDeltaTime = 1f / 30f;
                GameInput.HandleApplicationFocus(true);
                if (!GameSessionState.IsGameTimeRunning)
                    Assert.That(GameSessionState.TryStartPreparedGameTime() || GameSessionState.TryStartGameTimeFromWake(), Is.True);

                StairwellLabyrinthPuzzleInteraction first = null;
                foreach (var control in stairwell.LabyrinthPuzzles.Controls)
                    if (control.Id == "bird-0") { first = control; break; }
                Assert.That(first, Is.Not.Null);
                stairwell.Player.Motor.Teleport(first.Plan.EntryRootPosition);
                stairwell.Player.Motor.SetInputEnabled(true);
                stairwell.Player.Interactor.SetInputEnabled(true);
                yield return new WaitForSecondsRealtime(.2f);
                WorldItemPickup pickup = stairwell.LabyrinthPuzzles.PistolPickup;
                Assert.That(pickup, Is.Not.Null);
                Assert.That(pickup.CanInteract(stairwell.Player.Interactor), Is.False,
                    "The high counterweight must not allow the pistol to be taken before BirdLift.");
                Assert.That(GameSessionState.HasInventoryItem(InventoryItemId.CombatPistol), Is.False);
                float raisedItemHeight = pickup.Model.position.y;
                var combat = stairwell.Player.GameObject.GetComponent<PlayerCrowbarCombatController>();
                Assert.That(combat, Is.Not.Null);
                yield return WaitForCorruptedStairwell(() => combat.Actor != null, "The owned crowbar actor did not initialize.");
                CombatActor originalActor = combat.Actor;
                stairwell.Player.Motor.SetInputEnabled(false);

                yield return VerifyLabyrinthPuzzleActions(stairwell, 3, input, keyboard, captureFourThree: false);
                Assert.That(pickup.Model.position.y, Is.LessThan(raisedItemHeight - .75f),
                    "The completed gate must lower the actual pistol model into reach.");
                Assert.That(GameSessionState.HasInventoryItem(InventoryItemId.CombatPistol), Is.False,
                    "Solving the birds reveals the item; only the shared Take grants it.");
                yield return VerifyThirdPuzzlePistolPickup(stairwell, pickup, input, keyboard);
                stairwell.Player.Motor.SetInputEnabled(true);
                stairwell.Player.Interactor.SetInputEnabled(true);
                yield return WaitForCorruptedStairwell(() => combat.Actor.CarriedFirearm(CombatWeaponId.Pistol) != null,
                    "The acquired pistol was not added to the existing world combat actor.");
                Assert.That(combat.Actor, Is.SameAs(originalActor), "Acquiring a pistol must retain the live hero actor.");
                Assert.That(combat.Actor.CarriedWeapon(CombatWeaponId.Shotgun), Is.Null);
                Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.CombatPistol, true), Is.True);
                Assert.That(GameSessionState.IsInventoryItemEquipped(InventoryItemId.CombatCrowbar), Is.False);
                Assert.That(GameSessionState.IsInventoryItemEquipped(InventoryItemId.ChestFlashlight), Is.True);
                stairwell.Player.Motor.SetInputEnabled(true);
                stairwell.Player.Interactor.SetInputEnabled(true);
                yield return WaitForCorruptedStairwell(() => combat.Actor.HasWeaponInHand &&
                    combat.Actor.WeaponId == CombatWeaponId.Pistol && !combat.Actor.IsSwitchingWeapon,
                    "Equipping the found pistol did not complete its shared draw.");
                IFirearmState pistol = combat.Actor.CarriedFirearm(CombatWeaponId.Pistol);
                Assert.That(pistol.Rounds, Is.EqualTo(8));
                Assert.That(pistol.ReserveRounds, Is.Zero);
                // Leave the machinery before testing a valid shot. The pickup pose deliberately faces the tray.
                stairwell.Player.Motor.SetInputEnabled(false);
                var room = stairwell.LabyrinthPresentation.Rooms[2];
                yield return WalkStairwellWaypoint(stairwell, room.InteriorPoint);
                AimLabyrinthOrbit(stairwell, room.PassagePoint + Vector3.up * 1.4f);
                stairwell.Player.Motor.SetInputEnabled(true);
                yield return null;
                input.Press(mouse.rightButton, queueEventOnly: true);
                yield return WaitForThirdPuzzlePistolAim(stairwell, combat, pistol);
                yield return CaptureCorruptedGameplay(stairwell, "puzzle-third-pistol-aim", requireHero: false, captureFourThree: false);
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return WaitForCorruptedStairwell(() => pistol.Rounds == 7, "LMB did not fire the acquired pistol.");
                for (int frame = 0; frame < 15; frame++) yield return null;
                Assert.That(pistol.Rounds, Is.EqualTo(7), "Holding LMB must not turn the pistol into automatic fire.");
                input.Release(mouse.leftButton, queueEventOnly: true);
                input.Release(mouse.rightButton, queueEventOnly: true);
                yield return null;
                input.Press(keyboard.rKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.rKey, queueEventOnly: true);
                yield return null;
                Assert.That(pistol.IsReloading, Is.False, "The found pistol cannot reload without reserve ammunition.");
                Assert.That(pistol.Rounds, Is.EqualTo(7));

                yield return SceneManager.LoadSceneAsync(SceneIds.StairwellInterior, LoadSceneMode.Single);
                stairwell = Object.FindAnyObjectByType<StairwellInteriorRoot>();
                yield return WaitForCorruptedStairwell(() => stairwell != null && stairwell.IsInitialized,
                    "The retained pistol scene did not initialize.");
                Assert.That(stairwell.LabyrinthPuzzles.PistolPickup, Is.Null,
                    "Returning to the solved room must not recreate its collected pistol.");
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.CombatPistol), Is.EqualTo(1));
                Assert.That(GameSessionState.StairwellLabyrinthPuzzles.AllSolved, Is.True);
                yield return TransferThirdPuzzlePistolToWarehouse(stairwell, input, keyboard, 7, 0);
                stairwell = null;
                GameSessionState.BeginNewGame();
                Assert.That(GameSessionState.HasInventoryItem(InventoryItemId.CombatPistol), Is.False);
                Assert.That(GameSessionState.IsWorldItemCollected(BarPromenade.StairwellLabyrinthPuzzles.PistolSourceId), Is.False);
            }
            finally
            {
                if (stairwell != null && stairwell.LabyrinthPuzzles != null) stairwell.LabyrinthPuzzles.PuzzleMode.Cancel();
                Time.captureDeltaTime = previousDelta;
                if (mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
                GameInput.HandleApplicationFocus(previousFocus);
                GameSessionState.BeginNewGame();
            }
        }

        private static IEnumerator WaitForThirdPuzzlePistolAim(StairwellInteriorRoot scene,
            PlayerCrowbarCombatController combat, IFirearmState pistol)
        {
            float deadline = Time.realtimeSinceStartup + 8f;
            CombatActor actor = combat.Actor;
            Camera camera = scene.CameraFollow.Camera;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool ready = false;
                // The hero's Update deliberately unwinds additive IK before the graph evaluates.
                // Test the completed rendered muzzle, after the shared LateUpdate has reapplied it.
                yield return ScarfRenderedFrame(camera, () => ready = pistol.CanFire && actor.PistolAimAligned);
                if (ready) yield break;
            }
            string diagnostic = null;
            yield return ScarfRenderedFrame(camera, () => diagnostic = DescribeThirdPuzzlePistolAim(scene, actor, pistol));
            Debug.Log(diagnostic);
            yield return CaptureCorruptedGameplay(scene, "puzzle-third-pistol-aim-failure", requireHero: false, captureFourThree: false);
            Assert.Fail(diagnostic);
        }

        private static string DescribeThirdPuzzlePistolAim(StairwellInteriorRoot scene, CombatActor actor, IFirearmState pistol)
        {
            Camera camera = scene.CameraFollow.Camera;
            string diagnostic = "RMB did not settle the acquired pistol. " +
                $"canFire={pistol.CanFire},aim={pistol.AimRequested},aimProgress={pistol.AimProgress:F3}," +
                $"reload={pistol.ReloadPending},rounds={pistol.Rounds},aligned={actor.PistolAimAligned}," +
                $"aimError={actor.PistolAimErrorDegrees:F3},bodyAvailable={actor.PistolAimBodyAvailable}," +
                $"available={actor.IsAvailable},phase={actor.State.Phase},inHand={actor.HasWeaponInHand}," +
                $"switch={actor.IsSwitchingWeapon},focused={actor.CombatFocused},turn={actor.TurnScale:F3}," +
                $"root={actor.transform.position:F3},rootForward={actor.transform.forward:F3}," +
                $"muzzle={actor.PistolMuzzle.position:F3},muzzleForward={actor.PistolMuzzle.forward:F3}," +
                $"aimPoint={actor.PistolAimPoint:F3},aimIntent={actor.PistolAimIntentPoint:F3}," +
                $"camera={camera.transform.position:F3},cameraForward={camera.transform.forward:F3}," +
                $"freeAim={scene.CameraFollow.FreeAimActive},orbit={scene.FixedCamera.IsLabyrinthOrbit}," +
                $"input={GameInput.CanRead(GameInputContext.Gameplay)},motorInput={scene.Player.Motor.InputEnabled}," +
                $"interactorInput={scene.Player.Interactor.InputEnabled},interactClaim={scene.Player.Interactor.InteractKeyClaimed}," +
                $"RMB={GameInput.IsHeld(GameInputAction.MeleeBlock, GameInputContext.PauseMenu)}";
            return diagnostic + ",pose=" + actor.PistolPoseDiagnostics;
        }

        private static IEnumerator VerifyThirdPuzzlePistolPickup(StairwellInteriorRoot stairwell,
            WorldItemPickup pickup, InputTestFixture input, Keyboard keyboard)
        {
            Vector3 point = pickup.InteractionPosition;
            Vector3 intoRoom = stairwell.World.Root.TransformPoint(stairwell.LabyrinthPresentation.Rooms[2].InteriorPoint) - point;
            Vector3 stand = point + Vector3.ProjectOnPlane(intoRoom, Vector3.up).normalized * .85f;
            stand.y = stairwell.Player.GameObject.transform.position.y;
            stairwell.Player.Motor.Teleport(stand);
            stairwell.Player.Interactor.SetInputEnabled(true);
            AimLabyrinthOrbit(stairwell, stairwell.World.Root.InverseTransformPoint(point));
            Physics.SyncTransforms();
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(pickup.CanInteract(stairwell.Player.Interactor), Is.True);
            yield return CaptureCorruptedGameplay(stairwell, "puzzle-third-pistol-lowered", captureFourThree: false);
            var screen = WorldItemFoundScreen.For(stairwell.Player.Interactor);
            pickup.Interact(stairwell.Player.Interactor);
            yield return WaitForCorruptedStairwell(() => screen.IsShowing, "The shared pistol find screen did not settle.");
            Assert.That(screen.ActiveItemId, Is.EqualTo(InventoryItemId.CombatPistol));
            for (int frame = 0; frame <= 10; frame++)
            {
                foreach (Renderer renderer in pickup.Model.GetComponentsInChildren<Renderer>())
                    if (renderer.enabled) AssertPuzzleBoundsInFrame(stairwell.CameraFollow.Camera, renderer.bounds,
                        "rotating found pistol " + renderer.name);
                if (frame < 10) yield return null;
            }
            yield return CaptureCorruptedGameplay(stairwell, "puzzle-third-pistol-item-screen", requireHero: false, captureFourThree: false);
            input.Press(keyboard.escapeKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.escapeKey, queueEventOnly: true);
            yield return WaitForCorruptedStairwell(() => !screen.IsPresenting, "Cancelling the pistol find did not restore its tray.");
            Assert.That(GameSessionState.HasInventoryItem(InventoryItemId.CombatPistol), Is.False);
            Assert.That(pickup.Model.parent, Is.SameAs(pickup.transform));
            yield return null;
            stairwell.Player.Interactor.SetInputEnabled(true);
            pickup.Interact(stairwell.Player.Interactor);
            yield return WaitForCorruptedStairwell(() => screen.IsShowing, "The cancelled pistol could not be inspected again.");
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return WaitForCorruptedStairwell(() => !screen.IsPresenting, "Confirming the pistol find did not complete.");
            yield return null; // The confirming E remains held through the following world Update.
            input.Release(keyboard.eKey, queueEventOnly: true);
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.CombatPistol), Is.EqualTo(1));
            Assert.That(GameSessionState.TryCollectWorldItem(BarPromenade.StairwellLabyrinthPuzzles.PistolSourceId,
                InventoryItemId.CombatPistol), Is.False, "The third room's permanent source cannot grant twice.");
            yield return null;
        }

        private static IEnumerator TransferThirdPuzzlePistolToWarehouse(StairwellInteriorRoot stairwell,
            InputTestFixture input, Keyboard keyboard, int expectedRounds, int expectedReserve)
        {
            var door = stairwell.WarehouseEntrance;
            var target = door.GetComponent<PlayerDoorActionTarget>();
            Assert.That(target, Is.Not.Null);
            stairwell.Player.Motor.Teleport(target.Plan.EntryRootPosition);
            stairwell.Player.Motor.SetInputEnabled(true);
            stairwell.Player.Interactor.SetInputEnabled(true);
            GameInput.HandleApplicationFocus(true);
            Physics.SyncTransforms();
            yield return WaitForCorruptedStairwell(() => ReferenceEquals(stairwell.Player.Interactor.ActiveInteractable, door),
                "The final warehouse door did not offer its real E interaction.");
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return WaitForCorruptedStairwell(() => stairwell.AnimatedInteraction.IsActive,
                "The pistol must stow before the shared warehouse door action begins.");
            CombatTestRoot warehouse = null;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (SceneManager.GetActiveScene().name == SceneIds.CombatTest)
                    warehouse = Object.FindAnyObjectByType<CombatTestRoot>();
                if (warehouse != null && warehouse.IsInitialized && !SceneTransitionService.IsTransitioning) break;
                yield return null;
            }
            Assert.That(warehouse != null && warehouse.IsInitialized && !SceneTransitionService.IsTransitioning, Is.True,
                "The actual final-door transition did not finish loading the warehouse.");
            warehouse.SetSparring(false);
            warehouse.AutomaticOpponentDecisions = false;
            warehouse.SendMessage("SetRoomOpponentSuppression", true, SendMessageOptions.RequireReceiver);
            warehouse.SendMessage("OnApplicationFocus", true);
            GameInput.HandleApplicationFocus(true);
            Assert.That(warehouse.IsStoryContinuation, Is.True);
            Assert.That(warehouse.LoadoutSelection.Contains(CombatWeaponId.Crowbar), Is.True);
            Assert.That(warehouse.LoadoutSelection.Contains(CombatWeaponId.Pistol), Is.True);
            Assert.That(warehouse.LoadoutSelection.Contains(CombatWeaponId.Shotgun), Is.False);
            Assert.That(warehouse.Hero.CarriedWeapon(CombatWeaponId.Shotgun), Is.Null);
            IFirearmState pistol = warehouse.Hero.CarriedFirearm(CombatWeaponId.Pistol);
            Assert.That(pistol.Rounds, Is.EqualTo(expectedRounds), "The transition must retain the magazine already spent in the stairs.");
            Assert.That(pistol.ReserveRounds, Is.EqualTo(expectedReserve));
            Assert.That(warehouse.AmmoSpawns, Is.Not.Null, "A story pistol needs ammunition even without a shotgun.");
            Assert.That(warehouse.AmmoSpawns.SlotCount, Is.EqualTo(CombatAmmoSpawner.Capacity));
            for (int slot = 0; slot < warehouse.AmmoSpawns.SlotCount; slot++)
            {
                CombatAmmoPickup source = warehouse.AmmoSpawns.GetSlot(slot);
                if (slot % 2 == 0)
                {
                    Assert.That(source, Is.Not.Null, "Every authored pistol shelf must retain its supply.");
                    Assert.That(source.WeaponId, Is.EqualTo(CombatWeaponId.Pistol));
                }
                else Assert.That(source, Is.Null, "An unowned shotgun leaves its authored shelf empty.");
            }
            var probe = warehouse.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
            yield return AtWarehouseFridgePresentation(probe, () =>
                CaptureCurrentCamera(warehouse.CameraFollow.Camera, SceneIds.StairwellInterior, "puzzle-third-pistol-warehouse-arrival"));
            Object.Destroy(probe);

            CombatAmmoPickup pack = warehouse.AmmoSpawns.GetSlot(0);
            WarehouseLocker locker = null;
            int sourceBay = -1;
            foreach (var candidate in warehouse.GetComponentsInChildren<WarehouseLocker>())
                for (int bay = 0; bay < WarehouseLocker.DoorCount; bay++)
                    if (Vector3.Distance(candidate.ShelfPosition(bay), pack.transform.position) < .01f)
                    { locker = candidate; sourceBay = bay; break; }
            Assert.That(locker, Is.Not.Null);
            Vector3 approach = warehouse.WarehousePlan.AmmoApproaches[0];
            warehouse.Player.Motor.Teleport(approach + Vector3.up * PlayerFactory.GroundedRootOffset);
            warehouse.Hero.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(
                pack.transform.position - approach, Vector3.up).normalized);
            Assert.That(locker.BeginOpen(sourceBay), Is.True);
            locker.Advance(2f);
            Physics.SyncTransforms();
            yield return null;
            Assert.That(pack.TryBeginPickup(warehouse.Player.Interactor), Is.True);
            var found = WorldItemFoundScreen.For(warehouse.Player.Interactor);
            yield return WaitForCorruptedStairwell(() => found.IsShowing, "The pistol-only warehouse pack did not open.");
            yield return null;
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return WaitForCorruptedStairwell(() => !found.IsPresenting, "The pistol-only warehouse pack was not taken.");
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return null;
            Assert.That(pistol.ReserveRounds, Is.EqualTo(expectedReserve + 8));
            Assert.That(pistol.Rounds, Is.EqualTo(expectedRounds));
            Assert.That(warehouse.Hero.TrySelectWeapon(CombatWeaponId.Pistol), Is.True);
            yield return WaitForCorruptedStairwell(() => !warehouse.Hero.IsSwitchingWeapon &&
                warehouse.Hero.HasWeaponInHand && warehouse.Hero.WeaponId == CombatWeaponId.Pistol,
                "The retained pistol did not draw in the warehouse.");
            input.Press(keyboard.rKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.rKey, queueEventOnly: true);
            yield return WaitForCorruptedStairwell(() => pistol.Rounds == 8 && !pistol.ReloadPending,
                "R did not reload the story pistol from its newly collected finite reserve.");
            Assert.That(pistol.ReserveRounds, Is.EqualTo(expectedReserve + expectedRounds));
            Assert.That(warehouse.Hero.CarriedWeapon(CombatWeaponId.Shotgun), Is.Null);
        }
    }
}
