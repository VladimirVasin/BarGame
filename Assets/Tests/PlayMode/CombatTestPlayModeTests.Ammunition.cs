using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_AmmoReservesAndRespawningPickupsStayFinite()
        {
            VerifyFiniteAmmoTransactions();
            var input = new InputTestFixture();
            bool previousBackground = Application.runInBackground;
            try
            {
                input.Setup();
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                Application.runInBackground = true;
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = 0;
                var selection = new CombatTestSelection(new CombatLoadoutSelection(CombatWeaponId.Pistol),
                    CombatTestMode.Endless, new CombatEncounterSettings(1, 60f, 60f, 60f, 60f));
                Assert.That(CombatTestStartService.TryStart(selection), Is.True);
                yield return AwaitSelectedCombatRange();
                ForceAmmoFocus();
                yield return null;
                Assert.That(root.WarehousePlan, Is.Not.Null);
                Assert.That(root.LiveOpponentCount, Is.Zero);
                Assert.That(root.AmmoSpawns.SlotCount, Is.EqualTo(4));
                var pickups = new CombatAmmoPickup[4];
                var models = new Transform[4];
                for (int slot = 0; slot < pickups.Length; slot++)
                {
                    pickups[slot] = root.AmmoSpawns.GetSlot(slot);
                    models[slot] = pickups[slot].Model;
                    Assert.That(pickups[slot].WeaponId,
                        Is.EqualTo(slot % 2 == 0 ? CombatWeaponId.Pistol : CombatWeaponId.Shotgun));
                }
                AssertWarehouseAmmoPlacement(pickups, models);
                CombatAmmoPickup pistolPack = pickups[0], shotgunPack = pickups[1];
                IFirearmState pistol = root.Hero.CarriedFirearm(CombatWeaponId.Pistol);
                IFirearmState shotgun = root.Hero.CarriedFirearm(CombatWeaponId.Shotgun);
                WorldItemFoundScreen found = WorldItemFoundScreen.For(root.Player.Interactor);
                Assert.That(found.IsInitialized, Is.True);
                Assert.That(pistol.Rounds, Is.EqualTo(8));
                Assert.That(shotgun.Rounds, Is.EqualTo(2));
                Assert.That(pistol.ReserveRounds + shotgun.ReserveRounds, Is.Zero,
                    "A new run starts with only the rounds already loaded in both guns.");
                yield return CaptureAmmoScreen("Test empty starting ammunition reserve HUD");
                pistol.SetAim(true);
                pistol.Advance(pistol.Settings.RaiseSeconds);
                Assert.That(pistol.TryFire(), Is.True);
                pistol.Advance(pistol.Settings.FireCooldownSeconds);
                pistol.CancelAction();
                Assert.That(root.Hero.TryReloadPistol(), Is.False,
                    "Even a partly empty magazine cannot reload before a pack is collected.");

                ApproachAmmo(pistolPack);
                Assert.That(pistolPack.TryBeginPickup(root.Player.Interactor), Is.True);
                yield return WaitFor(() => found.IsShowing, "The ammunition find never reached its shared take screen.");
                yield return null;
                Assert.That(found.ActiveItemId, Is.EqualTo(InventoryItemId.CombatPistolAmmo));
                Assert.That(pistol.ReserveRounds, Is.Zero);
                Assert.That(found.Dismiss(), Is.True);
                yield return WaitFor(() => !found.IsPresenting, "Cancelling the ammunition find did not restore the world.");
                yield return null;
                Assert.That(pistolPack.IsAvailable && !pistolPack.IsPresenting, Is.True);
                Assert.That(pistolPack.Model.parent, Is.SameAs(pistolPack.transform));
                Assert.That(pistol.ReserveRounds, Is.Zero, "Looking at or cancelling a pack cannot grant it.");

                ForceAmmoFocus();
                Assert.That(pistolPack.TryBeginPickup(root.Player.Interactor), Is.True);
                yield return WaitFor(() => found.IsShowing, "The pistol pack did not reopen after cancellation.");
                yield return null;
                CaptureAmmoPreview("Test pistol ammunition preview");
                yield return CaptureAmmoScreen("Test ammunition found screen");
                ForceAmmoFocus();
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                yield return WaitFor(() => !found.IsPresenting, "Holding the actual take key did not finish the pickup.");
                yield return null; // The original E is still held during the next world Update.
                Assert.That(pistol.ReserveRounds, Is.EqualTo(8));
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.CombatPistolAmmo), Is.Zero,
                    "Polygon ammunition stays in the weapon reserve rather than the story inventory.");
                Assert.That(pistol.Rounds, Is.EqualTo(7), "A world pickup supplies reserve rather than the magazine.");
                Assert.That(pistolPack.IsAvailable || pistolPack.IsPresenting || found.IsPresenting, Is.False);
                Assert.That(found.Confirm(), Is.False, "A repeated confirmation cannot grant another pack.");
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return null;
                Assert.That(pistolPack.RespawnRemaining, Is.EqualTo(12f).Within(.001f));
                Assert.That(root.Hero.TryReloadPistol(), Is.True,
                    "Collecting a map pack enables the same reload that was refused before it.");
                root.Hero.Step(pistol.Settings.ReloadSeconds + .02f);
                Assert.That(pistol.Rounds, Is.EqualTo(8));
                Assert.That(pistol.ReserveRounds, Is.EqualTo(7), "Reload spends the collected reserve.");
                yield return CaptureAmmoScreen("Test finite ammunition HUD");

                Assert.That(root.PauseMenu.Open(), Is.True);
                root.Tick(20f);
                yield return null;
                Assert.That(pistolPack.RespawnRemaining, Is.EqualTo(12f).Within(.001f));
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Ammunition pause did not release input.");
                ForceAmmoFocus();

                ApproachAmmo(shotgunPack);
                Assert.That(root.Hero.CarriedWeaponLocation(CombatWeaponId.Shotgun), Is.EqualTo(CombatWeaponCarryLocation.Stowed));
                Assert.That(shotgunPack.TryBeginPickup(root.Player.Interactor), Is.True);
                yield return WaitFor(() => found.IsShowing, "The stowed shotgun's pack did not open.");
                yield return null;
                Assert.That(found.ActiveItemId, Is.EqualTo(InventoryItemId.CombatShotgunAmmo));
                CaptureAmmoPreview("Test shotgun ammunition preview");
                yield return CaptureAmmoScreen("Test shotgun ammunition found screen");
                root.Tick(20f);
                Assert.That(pistolPack.RespawnRemaining, Is.EqualTo(12f).Within(.001f), "A shared modal freezes every pack's return timer.");
                Assert.That(found.Confirm(), Is.True);
                Assert.That(found.Confirm(), Is.False);
                Assert.That(shotgun.ReserveRounds, Is.EqualTo(4));
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.CombatShotgunAmmo), Is.Zero);
                Assert.That(root.Hero.WeaponId, Is.EqualTo(CombatWeaponId.Pistol));
                yield return WaitFor(() => !found.IsPresenting, "The shotgun pack did not finish its shared return.");
                yield return null;
                ForceAmmoFocus();
                root.Tick(11.98f);
                Assert.That(pistolPack.IsAvailable || shotgunPack.IsAvailable, Is.False);
                Assert.That(pistolPack.RespawnRemaining,
                    Is.EqualTo(.02f).Within(CombatTestRoot.SimulationStep + .001f),
                    "The remaining time is quantized to the arena's fixed simulation step.");
                root.Tick(.05f);
                Assert.That(pistolPack.IsAvailable && shotgunPack.IsAvailable, Is.True);
                AssertWarehouseAmmoPlacement(pickups, models);

                Assert.That(pistol.AddReserveRounds(int.MaxValue), Is.EqualTo(41));
                ApproachAmmo(pistolPack);
                Assert.That(pistolPack.TryBeginPickup(root.Player.Interactor), Is.True);
                yield return WaitFor(() => found.IsShowing, "The full-reserve refusal did not open its find screen.");
                yield return null;
                Assert.That(found.Confirm(), Is.True);
                Assert.That(found.FeedbackKey, Is.EqualTo(WorldItemFoundScreen.NoRoomFeedbackKey));
                Assert.That(found.IsShowing && pistolPack.IsAvailable, Is.True);
                Assert.That(pistol.ReserveRounds, Is.EqualTo(48));
                yield return CaptureAmmoScreen("Test ammunition full reserve feedback");
                Assert.That(found.Dismiss(), Is.True);
                yield return WaitFor(() => !found.IsPresenting, "Full-reserve refusal trapped the hero in its modal.");
                yield return null;

                // Make one reserve space through the real finite state, then consume one whole pack once.
                pistol.SetAim(true);
                pistol.Advance(pistol.Settings.RaiseSeconds);
                Assert.That(pistol.TryFire(), Is.True);
                Assert.That(pistol.TryReload(), Is.True);
                pistol.Advance(pistol.Settings.ReloadSeconds);
                pistol.CancelAction();
                Assert.That(pistol.ReserveRounds, Is.EqualTo(47));
                root.Hero.ResumeForNextOpponent();
                Assert.That(pistol.Rounds, Is.EqualTo(8));
                Assert.That(pistol.ReserveRounds, Is.EqualTo(47), "Preparing the next opponent cannot refill the carried reserve.");
                ForceAmmoFocus();
                Assert.That(pistolPack.TryBeginPickup(root.Player.Interactor), Is.True);
                yield return WaitFor(() => found.IsShowing, "The partial-capacity pickup did not open.");
                yield return null;
                Assert.That(found.Confirm(), Is.True);
                Assert.That(pistol.ReserveRounds, Is.EqualTo(48));
                yield return WaitFor(() => !found.IsPresenting, "The partially accepted pack did not finish.");
                yield return null;
                root.SendMessage("OnApplicationFocus", false);
                GameInput.HandleApplicationFocus(false);
                root.Tick(3f);
                Assert.That(pistolPack.RespawnRemaining, Is.EqualTo(12f).Within(.001f));
                ForceAmmoFocus();
                root.enabled = false;
                root.Tick(20f);
                Assert.That(pistolPack.RespawnRemaining, Is.EqualTo(12f).Within(.001f));
                Assert.That(shotgunPack.CanInteract(root.Player.Interactor), Is.False);
                root.enabled = true;
                ForceAmmoFocus();
                root.Hero.State.ReceiveProjectileHit(root.Hero.State.Settings.MaxHealth);
                root.Tick(20f);
                Assert.That(pistolPack.RespawnRemaining, Is.EqualTo(12f).Within(.001f));
                Assert.That(shotgunPack.CanInteract(root.Player.Interactor), Is.False);

                root.ResetRound();
                Assert.That(root.Hero.State.IsDefeated, Is.False);
                Assert.That(pistol.Rounds, Is.EqualTo(8));
                Assert.That(shotgun.Rounds, Is.EqualTo(2));
                Assert.That(pistol.ReserveRounds + shotgun.ReserveRounds, Is.Zero,
                    "Reset restores loaded rounds without supplying a carried reserve.");
                AssertWarehouseAmmoPlacement(pickups, models);
                Assert.That(root.NewWarehouseLayout(1), Is.True);
                yield return null;
                AssertWarehouseAmmoPlacement(pickups, models);
                Assert.That(root.GetComponentsInChildren<CombatAmmoPickup>(true).Length, Is.EqualTo(4),
                    "Reset and layout replacement reuse the bounded four packs and their authored models.");
                Assert.That(root.Hero.CarriedFirearm(CombatWeaponId.Pistol), Is.SameAs(pistol));
                Assert.That(root.Hero.CarriedFirearm(CombatWeaponId.Shotgun), Is.SameAs(shotgun));

                ApproachAmmo(pistolPack);
                Assert.That(pistolPack.TryBeginPickup(root.Player.Interactor), Is.True);
                yield return WaitFor(() => found.IsShowing, "The cleanup pickup did not open.");
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return null;
                Assert.That(pistolPack == null && models[0] == null && found == null, Is.True,
                    "Scene unload releases the inspected model, shared screen and source together.");
                Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.True,
                    "An interrupted ammo pickup must release its modal ownership.");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null) WorldItemFoundScreen.For(root.Player.Interactor)?.Abandon();
                input.TearDown();
                Application.runInBackground = previousBackground;
            }
        }

        private void ForceAmmoFocus()
        {
            root.SendMessage("OnApplicationFocus", true);
            GameInput.HandleApplicationFocus(true);
        }

        private void ApproachAmmo(CombatAmmoPickup pickup)
        {
            int slot = Array.IndexOf(new[] { root.AmmoSpawns.GetSlot(0), root.AmmoSpawns.GetSlot(1),
                root.AmmoSpawns.GetSlot(2), root.AmmoSpawns.GetSlot(3) }, pickup);
            Assert.That(slot, Is.GreaterThanOrEqualTo(0));
            WarehouseLocker locker = Array.Find(root.GetComponentsInChildren<WarehouseLocker>(),
                candidate => Vector3.Distance(candidate.ShelfPosition(), pickup.transform.position) < .01f);
            Assert.That(locker, Is.Not.Null);
            Vector3 approach = root.WarehousePlan.AmmoApproaches[slot];
            root.Hero.ResetActor(approach + Vector3.up * PlayerFactory.GroundedRootOffset,
                Vector3.ProjectOnPlane(pickup.transform.position - approach, Vector3.up).normalized);
            Physics.SyncTransforms();
            ForceAmmoFocus();
            if (!locker.IsOpen)
            {
                Assert.That(locker.BeginOpen(), Is.True);
                locker.Advance(2f);
                Physics.SyncTransforms();
            }
            Assert.That(pickup.CanInteract(root.Player.Interactor), Is.True);
        }

        private void AssertWarehouseAmmoPlacement(CombatAmmoPickup[] pickups, Transform[] models)
        {
            var area = new WarehouseArenaArea(root.WarehousePlan);
            var floors = new int[3];
            for (int slot = 0; slot < pickups.Length; slot++)
            {
                CombatAmmoPickup pickup = root.AmmoSpawns.GetSlot(slot);
                Assert.That(pickup, Is.SameAs(pickups[slot]));
                Assert.That(pickup.Model, Is.SameAs(models[slot]));
                Assert.That(pickup.Model.parent, Is.SameAs(pickup.transform));
                Assert.That(pickup.IsAvailable && pickup.Model.gameObject.activeSelf, Is.True);
                Assert.That(pickup.RespawnRemaining, Is.Zero);
                Assert.That(area.TryGetHeight(root.WarehousePlan.AmmoApproaches[slot], out float floor, .5f), Is.True);
                Assert.That(pickup.transform.position, Is.EqualTo(root.WarehousePlan.AmmoAnchors[slot]));
                Assert.That(pickup.transform.position.y - floor, Is.EqualTo(1.065f).Within(.01f));
                Bounds bounds = WorldItemInspectionPresenter.CalculateWorldBounds(pickup.Model);
                Assert.That(bounds.min.y, Is.EqualTo(pickup.transform.position.y + .015f).Within(.01f));
                Assert.That(bounds.size.sqrMagnitude, Is.GreaterThan(.001f), "Each pack retains a visible authored model at its shelf contact.");
                int storey = Mathf.RoundToInt(floor / WarehouseArenaPlan.LevelRise);
                Assert.That(storey, Is.InRange(0, 2));
                floors[storey]++;
            }
            foreach (int count in floors)
                Assert.That(count, Is.GreaterThanOrEqualTo(1), "Seeded stock remains available on every floor.");
        }

        private void CaptureAmmoPreview(string name)
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, name + ".png");
            LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
            AreaCaptureFixture.CaptureCurrentCamera(root.CameraFollow.Camera, SceneIds.CombatTest, name);
        }

        private static IEnumerator CaptureAmmoScreen(string name)
        {
            if (Application.isBatchMode) yield break;
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", SceneIds.CombatTest, name + ".png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!File.Exists(path) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(File.Exists(path), Is.True, "The actual found-item UI/HUD frame was not written.");
        }

        private static void VerifyFiniteAmmoTransactions()
        {
            var pistol = new PistolState();
            Assert.That(pistol.ReserveRounds, Is.Zero);
            pistol.SetAim(true);
            pistol.Advance(pistol.Settings.RaiseSeconds);
            for (int round = 0; round < 8; round++)
            {
                Assert.That(pistol.TryFire(), Is.True);
                pistol.Advance(pistol.Settings.FireCooldownSeconds);
            }
            Assert.That(pistol.Rounds + pistol.ReserveRounds, Is.Zero);
            Assert.That(pistol.TryFire() || pistol.TryReload(), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => pistol.AddReserveRounds(-1));
            Assert.That(pistol.AddReserveRounds(3), Is.EqualTo(3));
            Assert.That(pistol.TryReload(), Is.True);
            pistol.Advance(1.79f);
            pistol.CancelAction();
            Assert.That(pistol.Rounds, Is.Zero);
            Assert.That(pistol.ReserveRounds, Is.EqualTo(3));
            Assert.That(pistol.TryReload(), Is.True);
            pistol.Advance(.01f);
            Assert.That(pistol.Rounds, Is.EqualTo(3));
            Assert.That(pistol.ReserveRounds, Is.Zero);

            pistol.Reset();
            Assert.That(pistol.Rounds, Is.EqualTo(8));
            Assert.That(pistol.ReserveRounds, Is.Zero);

            var shotgun = new ShotgunState();
            Assert.That(shotgun.ReserveRounds, Is.Zero);
            shotgun.SetAim(true);
            shotgun.Advance(shotgun.Settings.RaiseSeconds);
            for (int round = 0; round < 2; round++)
            {
                Assert.That(shotgun.TryFire(), Is.True);
                shotgun.Advance(shotgun.Settings.FireCooldownSeconds);
            }
            Assert.That(shotgun.TryReload(), Is.False, "Only the two loaded shells are available before a pickup.");
            Assert.That(shotgun.AddReserveRounds(1), Is.EqualTo(1));
            Assert.That(shotgun.TryReload(), Is.True);
            Assert.That(shotgun.ReloadChamberMask, Is.EqualTo(3));
            Assert.That(shotgun.ReloadLoadMask, Is.EqualTo(1));
            shotgun.Advance(shotgun.Settings.LoadFirstSeconds);
            Assert.That(shotgun.Rounds, Is.EqualTo(1));
            Assert.That(shotgun.ReserveRounds, Is.Zero);
            shotgun.CancelAction();
            Assert.That(shotgun.TryReload(), Is.True, "The action must close after its last reserve shell is inserted.");
            shotgun.Advance(shotgun.Settings.ReloadSeconds - shotgun.Settings.LoadFirstSeconds);
            Assert.That(shotgun.ChamberLoaded(0), Is.True);
            Assert.That(shotgun.ChamberLoaded(1), Is.False);
            Assert.That(shotgun.ReloadPending, Is.False);
            Assert.That(shotgun.TryReload(), Is.False);
            Assert.That(shotgun.AddReserveRounds(int.MaxValue), Is.EqualTo(16));
            Assert.That(shotgun.AddReserveRounds(1), Is.Zero);
            shotgun.Reset();
            Assert.That(shotgun.Rounds, Is.EqualTo(2));
            Assert.That(shotgun.ReserveRounds, Is.Zero);
        }
    }
}
