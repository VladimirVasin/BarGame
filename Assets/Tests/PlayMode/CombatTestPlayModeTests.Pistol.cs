using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        private readonly List<string> pistolGeometryIssues = new List<string>();

        [UnityTest]
        public IEnumerator Range_PistolSelectionProjectileFlightReloadAndResetUseTheLiveDuel()
        {
            pistolGeometryIssues.Clear();
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            Mouse mouse = null;
            GameObject obstacle = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                mouse = InputSystem.AddDevice<Mouse>();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                var menu = Object.FindAnyObjectByType<StartMenuRoot>();
                Assert.That(menu, Is.Not.Null);
                Assert.That(menu.SelectOption(StartMenuOption.CombatTest), Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(menu.IsChoosingCombatWeapon, Is.True);
                Assert.That(menu.SelectCombatOption(CombatPreparationOption.Pistol), Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(menu.SelectedCombatWeapon, Is.EqualTo(CombatWeaponId.Pistol));
                Assert.That(menu.SelectCombatOption(CombatPreparationOption.Start), Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                yield return AwaitSelectedCombatRange();
                Assert.That(root.HeroWeapon, Is.EqualTo(CombatWeaponId.Pistol));
                Assert.That(root.Hero.IsPistol, Is.True);
                Assert.That(root.Opponent.IsPistol, Is.False, "The opponent retains its original crowbar.");
                Assert.That(root.Hero.Weapon.name, Does.Contain("Pistol"));

                // Interactive play enables the journal by default; batch mode
                // must opt in to exercise the same late-frame snapshot path.
                string journalFolder = Path.Combine(Directory.GetCurrentDirectory(), "TestResults", "Test pistol duel diagnostics");
                root.SetDuelLogging(true, journalFolder);
                DuelJournal journal = root.JournalForDiagnostics;
                Assert.That(journal.Enabled, Is.True);
                root.CaptureJournalFrame();

                PlacePair(6f);
                root.Hero.SetPistolAim(true);
                Assert.That(root.Hero.RequestPistolShot(), Is.True, "A fresh trigger is resolved on the next completed simulation pose.");
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8), "An early raise request cannot spend a round.");
                Assert.That(root.Projectiles.SpawnCount, Is.Zero);
                root.Tick(root.Hero.Pistol.Settings.RaiseSeconds);
                Assert.That(root.Hero.Pistol.CanFire, Is.True);
                Assert.That(root.Projectiles.SpawnCount, Is.Zero, "Finishing the raise cannot replay its rejected trigger.");
                Assert.That(root.Hero.State.IsBlocking, Is.False, "Pistol aim never becomes the crowbar guard.");
                Vector3 chest = root.Opponent.Ragdoll.PhysicsController.ChestBody.position;
                Vector3 muzzle = root.Hero.PistolMuzzle.position;
                Vector3 muzzleForward = root.Hero.PistolMuzzle.forward;
                AssertPistolAimAligned("The raised gun's actual muzzle must face the opponent's chest");
                Assert.That(root.Hero.PistolSupportError, Is.LessThan(.02f));
                yield return CapturePistolNearViews("aim");
                yield return CaptureFocusGameView("pistol-01-aim");

                float health = root.Opponent.State.Health;
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                Assert.That(root.Projectiles.SpawnCount, Is.Zero, "Input queues a trigger; the final simulation pose owns its origin.");
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8));
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(1));
                Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(1));
                Assert.That(root.Opponent.State.Health, Is.EqualTo(health), "A new projectile cannot hit its distant target on the spawn substep.");
                Vector3 origin = root.Projectiles.LastPosition;
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(Vector3.Distance(origin, root.Projectiles.LastPosition), Is.InRange(1f, 3f),
                    "The next substep advances the actual bullet at its finite prototype speed.");
                Assert.That(root.Opponent.State.Health, Is.EqualTo(health));
                yield return CaptureFocusGameView("pistol-02-fired");
                for (int tick = 0; tick < 8 && root.Projectiles.ActiveCount > 0; tick++)
                    root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Projectiles.ActiveCount, Is.Zero);
                Assert.That(root.Projectiles.ImpactCount, Is.EqualTo(1));
                Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(1),
                    $"origin={origin:F4}, forward={muzzleForward:F4}, chest={chest:F4}, " +
                    $"lastPosition={root.Projectiles.LastPosition:F4}, worldOrBodyImpact={root.Projectiles.LastImpactPoint:F4}");
                Assert.That(root.Opponent.State.Health, Is.LessThan(health));
                Assert.That(root.Opponent.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Projectile));
                root.Tick(.5f);
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(1), "Advancing the duel cannot repeat a trigger request.");
                yield return CaptureFocusGameView("pistol-03-impact");

                // A 15 mm collider is crossed within a single 120 Hz step. A sampled
                // end-position overlap would miss it; the projectile must sweep its path.
                root.ResetRound();
                PlacePair(6f);
                obstacle = new GameObject("Test bullet obstacle");
                var wall = obstacle.AddComponent<BoxCollider>();
                wall.size = new Vector3(.015f, 1f, 1f);
                obstacle.transform.position = new Vector3(0f, 1.1f, -2f);
                Physics.SyncTransforms();
                Assert.That(root.Projectiles.TrySpawn(root.Hero, new Vector3(-2f, 1.1f, -2f),
                    Vector3.right * 250f, root.Hero.Pistol.ShotSequence), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Projectiles.ImpactCount, Is.EqualTo(1));
                Assert.That(root.Projectiles.ActiveCount, Is.Zero);
                Assert.That(Mathf.Abs(root.Projectiles.LastImpactPoint.x), Is.LessThan(.025f));
                Assert.That(root.Opponent.ReceivedImpactCount, Is.Zero, "A world contact cannot damage the unrelated opponent.");
                Object.DestroyImmediate(obstacle);
                obstacle = null;

                root.ResetRound();
                PlacePair(6f);
                Vector3 ballisticOrigin = new Vector3(-2f, 2f, -2f);
                Assert.That(root.Projectiles.TrySpawn(root.Hero, ballisticOrigin,
                    Vector3.right * CombatProjectilePool.MuzzleSpeed, root.Hero.Pistol.ShotSequence), Is.True);
                float flightSeconds = CombatTestRoot.SimulationStep * 3f;
                root.Tick(flightSeconds);
                Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(1));
                Assert.That(root.Projectiles.LastPosition.y,
                    Is.EqualTo(ballisticOrigin.y - .5f * CombatProjectilePool.Gravity * flightSeconds * flightSeconds).Within(.0001f),
                    "The physical projectile accumulates gravity during its elapsed flight.");

                // The real keyboard route must make R reload, keeping the existing
                // reset button/API independent from the newly assigned key.
                root.ResetRound();
                PlacePair(6f);
                root.Hero.SetPistolAim(true);
                root.Tick(.25f);
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                input.Press(keyboard.rKey, queueEventOnly: true);
                root.AutomaticSimulation = true;
                yield return null;
                root.AutomaticSimulation = false;
                input.Release(keyboard.rKey, queueEventOnly: true);
                InputSystem.Update();
                Assert.That(root.Hero.Pistol.IsReloading, Is.True, "R starts the magazine action instead of resetting the duel.");
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
                root.Tick(.9f);
                float reloadProgress = root.Hero.Pistol.ReloadProgress;
                Assert.That(reloadProgress, Is.GreaterThan(0f));
                yield return CapturePistolNearViews("reload");
                yield return CaptureFocusGameView("pistol-04-reload");
                Assert.That(root.PauseMenu.Open(), Is.True);
                root.Tick(.8f);
                yield return null;
                Assert.That(root.Hero.Pistol.ReloadProgress, Is.EqualTo(reloadProgress));
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release pistol gameplay.");
                Assert.That(root.SetOpponentFocus(false), Is.True);
                Assert.That(root.Hero.Pistol.IsReloading, Is.True, "Reloading remains available outside combat focus.");
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
                int reloadSpawns = root.Projectiles.SpawnCount;
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(reloadSpawns));
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
                root.Tick(1.3f);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8));
                Assert.That(root.Hero.Pistol.IsReloading, Is.False);
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(reloadSpawns), "Reload completion cannot release an old trigger.");
                Assert.That(root.SetOpponentFocus(true), Is.True);

                root.Hero.SetPistolAim(true);
                root.Tick(.25f);
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(.5f);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
                Assert.That(root.Hero.TryReloadPistol(), Is.True);
                root.Opponent.ResetActor(root.Hero.transform.position + root.Hero.transform.forward * 1.1f,
                    -root.Hero.transform.forward);
                Physics.SyncTransforms();
                Assert.That(root.Opponent.TryAttack(), Is.True);
                root.Tick(.8f);
                Assert.That(root.Hero.State.Health, Is.LessThan(MeleeCombatSettings.Crowbar.MaxHealth),
                    "The original opponent's real crowbar contact interrupts the magazine action.");
                Assert.That(root.Hero.Pistol.IsReloading, Is.False);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7), "An interrupted magazine action cannot refill its ammunition.");

                if (!root.Hero.IsKnockedDown)
                    Assert.That(root.Hero.TryBeginKnockdown(root.Hero.LastImpact, Vector3.zero, Vector3.zero), Is.True);
                Assert.That(root.Hero.IsWeaponDropped, Is.True, "A pistol is released on a living fall without a crowbar support solver.");
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
                root.ResetRound();
                Assert.That(root.Hero.IsWeaponDropped || root.Hero.IsKnockedDown, Is.False);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8));

                Assert.That(root.Projectiles.TrySpawn(root.Hero, new Vector3(-2f, 2f, -2f),
                    Vector3.right * 250f, root.Hero.Pistol.ShotSequence), Is.True);
                Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(1));
                root.ResetRound();
                Assert.That(root.HeroWeapon, Is.EqualTo(CombatWeaponId.Pistol));
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8));
                Assert.That(root.Projectiles.ActiveCount, Is.Zero, "Reset removes every old projectile before restoring the selected loadout.");
                Assert.That(root.Opponent.State.Health, Is.EqualTo(MeleeCombatSettings.Crowbar.MaxHealth));
                Assert.That(root.IsOpponentFocused, Is.True);

                yield return VerifyFreePistolAim(input, mouse);
                yield return VerifyPistolAimGeometryAndTriggerBoundaries(input, mouse);
                yield return VerifyPistolCarryAndWinnerInput(input, mouse, keyboard);

                Assert.That(CombatTestStartService.ReturnToPreparation(root.HeroWeapon), Is.True);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                    !SceneTransitionService.IsTransitioning, "Combat preparation did not reopen.");
                yield return WaitFor(() => journal.Completion.IsCompleted, "Pistol journal did not close with its scene.");
                Assert.That(journal.LastError, Is.Null);
                AssertPistolJournalSnapshots(journalFolder, journal.SessionId);
                menu = Object.FindAnyObjectByType<StartMenuRoot>();
                Assert.That(menu.IsChoosingCombatWeapon, Is.True);
                Assert.That(menu.SelectedCombatWeapon, Is.EqualTo(CombatWeaponId.Pistol));
                Assert.That(menu.SelectCombatOption(CombatPreparationOption.Crowbar), Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(menu.SelectCombatOption(CombatPreparationOption.Start), Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                yield return AwaitSelectedCombatRange();
                Assert.That(root.HeroWeapon, Is.EqualTo(CombatWeaponId.Crowbar));
                Assert.That(root.Hero.IsPistol || root.Opponent.IsPistol, Is.False);
                PlacePair(1.1f);
                Assert.That(root.Hero.TryAttack(), Is.True, "Choosing the crowbar retains its original attack path.");
                root.Tick(MeleeCombatSettings.Crowbar.WindupSeconds + MeleeCombatSettings.Crowbar.ActiveSeconds + .05f);
                Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(1));
                Assert.That(root.Opponent.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Weapon));
                float crowbarHealth = root.Opponent.State.Health;
                input.Press(keyboard.rKey, queueEventOnly: true);
                root.AutomaticSimulation = true;
                yield return null;
                root.AutomaticSimulation = false;
                input.Release(keyboard.rKey, queueEventOnly: true);
                InputSystem.Update();
                Assert.That(root.Opponent.State.Health, Is.EqualTo(crowbarHealth), "R cannot reset a crowbar round after becoming the reload key.");
                LogAssert.NoUnexpectedReceived();
                Assert.That(pistolGeometryIssues, Is.Empty, string.Join("\n", pistolGeometryIssues));
            }
            finally
            {
                if (root != null)
                {
                    root.AutomaticSimulation = false;
                    root.SetDuelLogging(false);
                }
                if (obstacle != null) Object.DestroyImmediate(obstacle);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                input.TearDown();
            }
        }

        private IEnumerator VerifyFreePistolAim(InputTestFixture input, Mouse mouse)
        {
            root.SetSparring(false);
            PlacePair(6f);
            root.SendMessage("OnApplicationFocus", true);
            Assert.That(root.SetOpponentFocus(false), Is.True);
            CursorLockMode cursorLock = Cursor.lockState;
            bool cursorVisible = Cursor.visible;
            root.AutomaticSimulation = true;
            input.Press(mouse.rightButton, queueEventOnly: true);
            yield return null;
            Assert.That(root.Hero.Pistol.AimRequested, Is.True, "RMB raises the pistol without reacquiring the opponent.");
            Assert.That(root.CameraFollow.FreeAimActive, Is.True);
            Assert.That(root.IsOpponentFocused || root.CameraFollow.TargetLockActive || root.Player.Motor.MovementTargetActive, Is.False);
            root.Tick(.3f);
            Assert.That(root.Hero.Pistol.CanFire, Is.True);
            Assert.That(((Player3DCharacterPresentation)root.Player.Visual).OwnsClip(root.Hero), Is.True);

            Camera camera = root.CameraFollow.Camera;
            float yaw = camera.transform.eulerAngles.y;
            float pitch = camera.transform.eulerAngles.x;
            input.Set(mouse.delta, new Vector2(180f, -35f), queueEventOnly: true);
            yield return null;
            Assert.That(Mathf.DeltaAngle(yaw, camera.transform.eulerAngles.y), Is.EqualTo(28.8f).Within(.1f),
                "The simulation and LateUpdate must consume a mouse delta only once.");
            Assert.That(Mathf.DeltaAngle(pitch, camera.transform.eulerAngles.x), Is.EqualTo(4.9f).Within(.1f));
            root.Tick(.5f);
            AssertPistolAimAligned("A settled free aim must pass the same gate used by shot commit");
            Assert.That(root.Hero.PistolSupportError, Is.LessThan(.02f));
            yield return CaptureFocusGameView("pistol-05-free-aim");

            // Both ends of the gun can be past a thin wall; clearance must
            // include the reach from the body, not just the weapon's own length.
            Vector3 chest = root.Hero.Ragdoll.PhysicsController.ChestBody.position;
            Vector3 grip = root.Hero.Weapon.transform.position;
            Vector3 clearAimPoint = root.Hero.PistolAimPoint;
            var barrier = new GameObject("Test close aiming wall");
            var shape = barrier.AddComponent<BoxCollider>();
            shape.size = new Vector3(.3f, .3f, .015f);
            barrier.transform.SetPositionAndRotation(Vector3.Lerp(chest, grip, .5f), Quaternion.LookRotation(grip - chest));
            Physics.SyncTransforms();
            try
            {
                Assert.That(root.Projectiles.MuzzleIsClear(root.Hero, root.Hero.PistolMuzzle.position), Is.False);
                // Keep the accepted distant target: a fresh camera ray would
                // select this newly inserted barrier as a different aim point.
                root.Hero.SetPistolAim(true, clearAimPoint);
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8), "A held gun beyond the wall must not spend a round.");
                Assert.That(root.Projectiles.SpawnCount, Is.Zero, "A held gun beyond the wall must not spawn a bullet through it.");
            }
            finally { Object.DestroyImmediate(barrier); }

            input.Press(mouse.leftButton, queueEventOnly: true);
            input.Set(mouse.delta, new Vector2(1125f, 0f), queueEventOnly: true);
            yield return null;
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8), "A half-turn cannot fire through the old body facing.");
            Assert.That(root.Projectiles.SpawnCount, Is.Zero);
            root.Tick(1.5f);
            yield return null;
            Assert.That(root.Projectiles.SpawnCount, Is.Zero, "Settling with LMB held must not release a deferred shot.");
            input.Release(mouse.leftButton, queueEventOnly: true);
            yield return null;

            // Fire on the same frame as another mouse turn: the shot must use
            // that accepted look, rather than the preceding rendered frame.
            float health = root.Opponent.State.Health;
            input.Press(mouse.leftButton, queueEventOnly: true);
            input.Set(mouse.delta, new Vector2(45f, 0f), queueEventOnly: true);
            yield return null;
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(1));
            Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(1));
            Vector3 position = root.Projectiles.LastPosition;
            Vector3 shotForward = (root.Hero.PistolAimPoint - position).normalized;
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(Vector3.Dot((root.Projectiles.LastPosition - position).normalized, shotForward), Is.GreaterThan(.99f));
            root.Tick(.1f);
            Assert.That(root.Opponent.State.Health, Is.EqualTo(health), "Free aiming away from the opponent must not auto-target it.");
            input.Release(mouse.leftButton, queueEventOnly: true);
            yield return null;

            Assert.That(root.PauseMenu.Open(), Is.True);
            yield return null;
            Assert.That(root.CameraFollow.FreeAimActive || root.Hero.Pistol.AimRequested, Is.False);
            Assert.That(Cursor.lockState, Is.EqualTo(cursorLock));
            Assert.That(Cursor.visible, Is.EqualTo(cursorVisible));
            input.Release(mouse.rightButton, queueEventOnly: true);
            yield return null;
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release free aiming.");
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(1));

            input.Press(mouse.rightButton, queueEventOnly: true);
            yield return null;
            Assert.That(root.CameraFollow.FreeAimActive, Is.True);
            root.SendMessage("OnApplicationFocus", false);
            yield return null;
            Assert.That(root.CameraFollow.FreeAimActive || root.Hero.Pistol.AimRequested, Is.False);
            Assert.That(Cursor.lockState, Is.EqualTo(cursorLock));
            root.SendMessage("OnApplicationFocus", true);
            yield return null;
            Assert.That(root.Hero.Pistol.AimRequested, Is.False, "Returning focus with RMB held must wait for its release.");
            input.Release(mouse.rightButton, queueEventOnly: true);
            yield return null;
            input.Press(mouse.rightButton, queueEventOnly: true);
            yield return null;
            Assert.That(root.CameraFollow.FreeAimActive, Is.True);
            Assert.That(root.SetOpponentFocus(true), Is.True);
            Assert.That(root.CameraFollow.FreeAimActive, Is.False);
            Assert.That(root.CameraFollow.TargetLockActive, Is.True);
            Assert.That(Cursor.lockState, Is.EqualTo(cursorLock));
            input.Release(mouse.rightButton, queueEventOnly: true);
            yield return null;
            root.AutomaticSimulation = false;
            root.SetSparring(true);
        }

        private IEnumerator VerifyPistolAimGeometryAndTriggerBoundaries(InputTestFixture input, Mouse mouse)
        {
            root.AutomaticSimulation = false;
            PlacePair(6f);
            Assert.That(root.SetOpponentFocus(false), Is.True);
            Assert.That(root.Hero.CombatFocused, Is.False);
            Vector3[] targets =
            {
                new Vector3(.75f, .2f, 0f),
                new Vector3(1f, 1.35f, 0f),
                new Vector3(4f, 1.35f, 0f),
                new Vector3(20f, 1.35f, 0f)
            };
            string[] names = { "close low", "close level", "mid range", "far range" };
            for (int index = 0; index < targets.Length; index++)
            {
                root.Hero.SetPistolAim(true, targets[index]);
                Assert.That(root.Hero.PistolAimPoint, Is.EqualTo(targets[index]), names[index]);
                if (index == 0)
                {
                    for (int step = 0; step < 10; step++)
                    {
                        root.Tick(.025f);
                        ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                        AssertPistolArmsClearBody($"Pistol raise step {step + 1}");
                    }
                    root.Tick(.95f);
                }
                else root.Tick(1.2f);
                AssertPistolAimAligned(names[index]);
                Assert.That(root.Hero.PistolSupportError, Is.LessThan(.02f), names[index]);
                int spawns = root.Projectiles.SpawnCount, rounds = root.Hero.Pistol.Rounds;
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(spawns + 1), names[index]);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(rounds - 1), names[index]);
            }

            VerifyPistolTriggerStepBoundaries();

            // Use the player's RMB and mouse path as well as explicit aim points.
            // The old single-pass shoulder correction stayed over five degrees
            // away from this nearby floor point even after the body had settled.
            PlacePair(6f);
            Assert.That(root.SetOpponentFocus(false), Is.True);
            Assert.That(root.Hero.CombatFocused, Is.False);
            root.AutomaticSimulation = true;
            input.Press(mouse.rightButton, queueEventOnly: true);
            yield return null;
            Assert.That(root.Hero.Pistol.AimRequested && root.CameraFollow.FreeAimActive, Is.True);
            // Flush RMB before queuing another full mouse state; otherwise the
            // LMB event can replace the still-pending RMB press with released.
            input.Press(mouse.leftButton, queueEventOnly: true);
            yield return null;
            root.AutomaticSimulation = false;
            Assert.That(root.Projectiles.SpawnCount, Is.Zero, "An early mouse trigger must be rejected during the raise.");
            root.Tick(.4f);
            root.AutomaticSimulation = true;
            yield return null;
            Assert.That(root.Projectiles.SpawnCount, Is.Zero, "Holding the early trigger cannot fire when the raise completes.");
            input.Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            yield return VerifyMouseAimAtNearbyFloor(input, mouse);
            root.AutomaticSimulation = true;
            input.Release(mouse.rightButton, queueEventOnly: true);
            yield return null;
            root.AutomaticSimulation = false;
            root.Hero.SetPistolAim(false);
            for (int step = 0; step < 10; step++)
            {
                root.Tick(.025f);
                ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                AssertPistolArmsClearBody($"Pistol lower step {step + 1}");
            }
            root.ResetRound();
        }

        private void VerifyPistolTriggerStepBoundaries()
        {
            PlacePair(6f);
            root.Opponent.ResetActor(Vector3.left * 6f + Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.right);
            Physics.SyncTransforms();
            Assert.That(root.SetOpponentFocus(false), Is.True);
            Assert.That(root.Hero.CombatFocused, Is.False);
            root.Hero.SetPistolAim(true, new Vector3(0f, 1.35f, 20f));
            Assert.That(root.Hero.PistolAimPoint, Is.EqualTo(new Vector3(0f, 1.35f, 20f)));
            float step = CombatTestRoot.SimulationStep;
            root.Tick(root.Hero.Pistol.Settings.RaiseSeconds - step);
            Assert.That(root.Hero.Pistol.IsRaising, Is.True);
            Assert.That(root.Hero.RequestPistolShot(), Is.True);
            root.Tick(step);
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(1), "The current substep's completed raise must be visible to commit.");
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));

            root.Tick(root.Hero.Pistol.Settings.FireCooldownSeconds - step);
            Assert.That(root.Hero.Pistol.CooldownRemaining, Is.GreaterThan(0f));
            Assert.That(root.Hero.RequestPistolShot(), Is.True);
            root.Tick(step);
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(2), "The current substep's expired cooldown must be visible to commit.");
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(6));

            Assert.That(root.Hero.RequestPistolShot(), Is.True);
            root.Tick(step);
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(2));
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(6));
            root.Tick(.6f);
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(2), "A rejected cooldown request cannot wait for a later ready step.");
        }

        private IEnumerator VerifyMouseAimAtNearbyFloor(InputTestFixture input, Mouse mouse)
        {
            Assert.That(root.CameraFollow.FreeAimActive, Is.True);
            Camera camera = root.CameraFollow.Camera;
            float yaw = camera.transform.eulerAngles.y;
            float pitch = Mathf.DeltaAngle(0f, camera.transform.eulerAngles.x);
            input.Set(mouse.delta, new Vector2(Mathf.DeltaAngle(yaw, 90f) / .16f, (pitch - 55f) / .14f), queueEventOnly: true);
            root.AutomaticSimulation = true;
            yield return null;
            root.AutomaticSimulation = false;
            root.Tick(1.2f);
            root.AutomaticSimulation = true;
            yield return null;
            root.AutomaticSimulation = false;
            Vector3 target = root.Hero.PistolAimPoint;
            Assert.That(target.y, Is.LessThan(.2f), "Mouse aiming down must select the actual nearby floor.");
            Assert.That(Vector3.Distance(target, root.Hero.transform.position), Is.InRange(.5f, 2f));
            AssertPistolAimAligned("Mouse aim at the nearby floor must converge after settling");
            Assert.That(root.Hero.PistolSupportError, Is.LessThan(.02f));
            var hands = root.Hero.GetComponentInChildren<NpcHandPose>();
            float gripForward = Vector3.Dot(hands.CylinderCentre(false) - root.Hero.transform.position,
                root.Hero.transform.forward);
            Assert.That(gripForward, Is.GreaterThanOrEqualTo(.30f),
                $"Low aim must keep the grip ahead of the torso; forward={gripForward:F4}m.");
            yield return CapturePistolNearViews(root.RoundFinished ? "winner-aim-low" : "aim-low");

            int spawns = root.Projectiles.SpawnCount, rounds = root.Hero.Pistol.Rounds;
            int impacts = root.Projectiles.ImpactCount;
            bool finished = root.RoundFinished;
            float opponentHealth = root.Opponent.State.Health;
            input.Press(mouse.leftButton, queueEventOnly: true);
            root.AutomaticSimulation = true;
            yield return null;
            root.AutomaticSimulation = false;
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(spawns + 1));
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(rounds - 1));
            root.Tick(.2f);
            Assert.That(root.Projectiles.ImpactCount, Is.EqualTo(impacts + 1));
            Assert.That(Vector3.Distance(root.Projectiles.LastImpactPoint, target), Is.LessThan(.15f));
            root.Tick(.5f);
            root.AutomaticSimulation = true;
            yield return null;
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(spawns + 1), "Holding LMB after a floor shot cannot repeat it.");
            Assert.That(root.RoundFinished, Is.EqualTo(finished));
            Assert.That(root.Opponent.State.Health, Is.EqualTo(opponentHealth));
            input.Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            root.AutomaticSimulation = false;
        }

        private void AssertPistolAimAligned(string context)
        {
            // A coroutine resumes before LateUpdate. Measure the completed
            // firearm pose used by Present/commit, not the graph-only interim pose.
            ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
            string detail = $"{context}: error={root.Hero.PistolAimErrorDegrees:F3}, " +
                $"origin={root.Hero.PistolMuzzle.position:F4}, forward={root.Hero.PistolMuzzle.forward:F4}, target={root.Hero.PistolAimPoint:F4}";
            Assert.That(root.Hero.PistolAimAligned, Is.True, detail);
            Assert.That(root.Hero.PistolAimErrorDegrees, Is.LessThanOrEqualTo(CombatActor.MaximumPistolAimErrorDegrees), detail);
            AssertPistolWrist(context, 35f);
            AssertPistolWrist(context, 45f, true);
            AssertPistolHandsClear(context);
            AssertPistolArmsClearBody(context);
        }

        private void AssertPistolWrist(string context, float maximumBend, bool isLeft = false)
        {
            var hands = root.Hero.GetComponentInChildren<NpcHandPose>();
            NpcHandPose.HandBinding binding = null;
            foreach (NpcHandPose.HandBinding hand in hands.Hands)
                if (hand.IsLeft == isLeft) binding = hand;
            Assert.That(binding, Is.Not.Null);
            // Remove the palm's depth offset to recover the actual hand length,
            // independently of source/FBX bone axes and their handedness.
            Vector3 distal = Vector3.ProjectOnPlane(hands.CylinderCentre(isLeft) - binding.Hand.position,
                hands.PalmNormal(isLeft)).normalized;
            string side = isLeft ? "left" : "right";
            Transform forearm = CityPedestrianHandProps.FindSocket(root.Hero.DamageRigRoot, isLeft ? "forearm.L" : "forearm.R");
            Assert.That(forearm, Is.Not.Null);
            float bend = Vector3.Angle(binding.Hand.position - forearm.position, distal);
            Assert.That(Vector3.Dot(distal, root.Hero.PistolMuzzle.forward), Is.GreaterThan(.99f),
                context + $": the barrel must follow the {side} hand's length.");
            if (!(bend < maximumBend))
                RecordPistolGeometryIssue(context + $": {side} wrist bends {bend:F2} degrees; limit={maximumBend:F2}.");
        }

        private sealed class PistolHandSurface
        {
            public string Name;
            public Vector3[] Vertices;
            public int[] Triangles;
            public Bounds Bounds;
            public Vector3 ShoulderSeamOrigin, ShoulderSeamAxis;
            public float ShoulderSeamAllowance;
        }

        private static Vector3 pistolEdgeIntersection;

        private void AssertPistolHandsClear(string context)
        {
            // Anchor contact alone cannot detect fingers passing through the
            // other palm. Check the visible, skinned hand geometry as rendered.
            var sides = new[] { new List<PistolHandSurface>(), new List<PistolHandSurface>() };
            var scratch = new Mesh();
            try
            {
                var hands = root.Hero.GetComponentInChildren<NpcHandPose>();
                foreach (NpcHandPose.HandBinding hand in hands.Hands)
                foreach (SkinnedMeshRenderer skin in hand.Renderers)
                    sides[hand.IsLeft ? 0 : 1].Add(BakePistolSurface(skin, scratch));
            }
            finally { Object.DestroyImmediate(scratch); }
            Assert.That(sides[0].Count, Is.EqualTo(6));
            Assert.That(sides[1].Count, Is.EqualTo(6));
            foreach (PistolHandSurface left in sides[0])
            foreach (PistolHandSurface right in sides[1])
                AssertPistolSurfacesClear(left, right, context);
        }

        private void AssertPistolArmsClearBody(string context)
        {
            var arms = new List<PistolHandSurface>();
            var body = new List<PistolHandSurface>();
            var scratch = new Mesh();
            try
            {
                var registry = ((Player3DCharacterPresentation)root.Player.Visual).Registry;
                foreach (Player3DMeshBinding binding in registry.MeshBindings)
                {
                    bool lowerArm = binding.BodyGroup == "LeftLowerArm" || binding.BodyGroup == "RightLowerArm";
                    bool upperArm = binding.BodyGroup == "LeftUpperArm" || binding.BodyGroup == "RightUpperArm";
                    if (!lowerArm && !upperArm && binding.BodyGroup != "Body") continue;
                    Renderer renderer = binding.Renderer;
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff)
                        continue;
                    Assert.That(renderer, Is.InstanceOf<SkinnedMeshRenderer>(), "Visible hero surfaces must use the world rig.");
                    PistolHandSurface surface = BakePistolSurface((SkinnedMeshRenderer)renderer, scratch);
                    if (upperArm)
                    {
                        Transform shoulder = CityPedestrianHandProps.FindSocket(root.Hero.DamageRigRoot, binding.BoneName);
                        Transform elbow = CityPedestrianHandProps.FindSocket(root.Hero.DamageRigRoot,
                            binding.AnatomicalSide == "Left" ? "forearm.L" : "forearm.R");
                        Assert.That(shoulder, Is.Not.Null);
                        Assert.That(elbow, Is.Not.Null);
                        surface.ShoulderSeamOrigin = shoulder.position;
                        surface.ShoulderSeamAxis = (elbow.position - shoulder.position).normalized;
                        surface.ShoulderSeamAllowance = .10f;
                    }
                    (lowerArm || upperArm ? arms : body).Add(surface);
                }
            }
            finally { Object.DestroyImmediate(scratch); }
            Assert.That(arms.Count, Is.GreaterThan(0), "Visible forearms and hands must be measured.");
            Assert.That(body.Count, Is.GreaterThan(0), "Visible torso surfaces must be measured.");
            // Preserve only the natural shoulder attachment; the rest of each
            // upper arm, and every forearm/cuff/hand triangle, must clear the body.
            foreach (PistolHandSurface arm in arms)
            foreach (PistolHandSurface torso in body)
                AssertPistolSurfacesClear(arm, torso, context);
        }

        private static PistolHandSurface BakePistolSurface(SkinnedMeshRenderer skin, Mesh scratch)
        {
            skin.BakeMesh(scratch, true);
            Vector3[] vertices = scratch.vertices;
            var bounds = new Bounds();
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = skin.transform.TransformPoint(vertices[i]);
                if (i == 0) bounds = new Bounds(vertices[i], Vector3.zero);
                else bounds.Encapsulate(vertices[i]);
            }
            return new PistolHandSurface
            {
                Name = skin.name, Vertices = vertices,
                Triangles = scratch.triangles, Bounds = bounds
            };
        }

        private void AssertPistolSurfacesClear(PistolHandSurface left, PistolHandSurface right, string context)
        {
            if (!left.Bounds.Intersects(right.Bounds)) return;
            for (int l = 0; l < left.Triangles.Length; l += 3)
            {
                if (IsPistolShoulderSeam(left, l)) continue;
                Vector3 a = left.Vertices[left.Triangles[l]], b = left.Vertices[left.Triangles[l + 1]],
                    c = left.Vertices[left.Triangles[l + 2]];
                for (int r = 0; r < right.Triangles.Length; r += 3)
                {
                    if (IsPistolShoulderSeam(right, r)) continue;
                    Vector3 d = right.Vertices[right.Triangles[r]], e = right.Vertices[right.Triangles[r + 1]],
                        f = right.Vertices[right.Triangles[r + 2]];
                    if (PistolHandEdgeCrosses(a, b, d, e, f) || PistolHandEdgeCrosses(b, c, d, e, f) ||
                        PistolHandEdgeCrosses(c, a, d, e, f) || PistolHandEdgeCrosses(d, e, a, b, c) ||
                        PistolHandEdgeCrosses(e, f, a, b, c) || PistolHandEdgeCrosses(f, d, a, b, c))
                    {
                        Vector3 leftCentroid = (a + b + c) / 3f, rightCentroid = (d + e + f) / 3f;
                        float leftProjection = Vector3.Dot(leftCentroid - left.ShoulderSeamOrigin, left.ShoulderSeamAxis);
                        float rightProjection = Vector3.Dot(rightCentroid - right.ShoulderSeamOrigin, right.ShoulderSeamAxis);
                        RecordPistolGeometryIssue($"{context}: visible surfaces intersect: {left.Name} / {right.Name}; " +
                            $"triangles={l / 3}/{r / 3}, centroids={leftCentroid:F5}/{rightCentroid:F5}, " +
                            $"edgeBarycentricAndTime={pistolEdgeIntersection:F8}, " +
                            $"seamProjection={leftProjection:F5}/{rightProjection:F5}, " +
                            $"seamAllowance={left.ShoulderSeamAllowance:F3}/{right.ShoulderSeamAllowance:F3}.");
                        return;
                    }
                }
            }
        }

        private void RecordPistolGeometryIssue(string issue)
        {
            Transform frame = root.Hero.transform;
            string detail = issue + $" hero={frame.position:F4}, yaw={frame.eulerAngles.y:F2}";
            foreach (string side in new[] { "R", "L" })
            {
                foreach (string joint in new[] { "upper_arm", "forearm", "hand" })
                {
                    Transform bone = CityPedestrianHandProps.FindSocket(root.Hero.DamageRigRoot, joint + "." + side);
                    detail += bone != null ? $" {joint}.{side}Local={frame.InverseTransformPoint(bone.position):F4}"
                        : $" {joint}.{side}=missing";
                }
            }
            detail += $" target={root.Hero.PistolAimPoint:F4}, targetLocal={frame.InverseTransformPoint(root.Hero.PistolAimPoint):F4}, " +
                $"muzzle={root.Hero.PistolMuzzle.position:F4}, muzzleLocal={frame.InverseTransformPoint(root.Hero.PistolMuzzle.position):F4}, " +
                $"muzzleForward={root.Hero.PistolMuzzle.forward:F4}.";
            pistolGeometryIssues.Add(detail);
            LogAssert.Expect(LogType.Warning, detail);
            Debug.LogWarning(detail);
        }

        private static bool IsPistolShoulderSeam(PistolHandSurface surface, int triangle)
        {
            if (surface.ShoulderSeamAllowance <= 0f) return false;
            Vector3 centroid = (surface.Vertices[surface.Triangles[triangle]] +
                surface.Vertices[surface.Triangles[triangle + 1]] +
                surface.Vertices[surface.Triangles[triangle + 2]]) / 3f;
            return Vector3.Dot(centroid - surface.ShoulderSeamOrigin, surface.ShoulderSeamAxis) < surface.ShoulderSeamAllowance;
        }

        private static bool PistolHandEdgeCrosses(Vector3 start, Vector3 end, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 edge = end - start, ab = b - a, ac = c - a;
            Vector3 cross = Vector3.Cross(edge, ac);
            float determinant = Vector3.Dot(ab, cross);
            if (Mathf.Abs(determinant) < 1e-10f) return false;
            float inverse = 1f / determinant;
            Vector3 fromA = start - a;
            float u = Vector3.Dot(fromA, cross) * inverse;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(fromA, ab);
            float v = Vector3.Dot(edge, q) * inverse;
            if (v < 0f || u + v > 1f) return false;
            float t = Vector3.Dot(ac, q) * inverse;
            // Surface contact is allowed; an edge must enter the other mesh.
            float endpointTolerance = .00002f / Mathf.Max(edge.magnitude, .00002f);
            bool crosses = t > endpointTolerance && t < 1f - endpointTolerance;
            if (crosses) pistolEdgeIntersection = new Vector3(u, v, t);
            return crosses;
        }

        private IEnumerator VerifyPistolCarryAndWinnerInput(InputTestFixture input, Mouse mouse, Keyboard keyboard)
        {
            PlacePair(6f);
            Assert.That(root.SetOpponentFocus(false), Is.True);
            root.Tick(.6f);
            for (int frame = 0; frame < 24; frame++) yield return null;
            AssertPistolLowCarry();
            yield return CapturePistolNearViews("idle-free");

            // Win with the same live projectiles used in play, then exercise
            // the winner rather than silently starting another duel.
            Assert.That(root.SetOpponentFocus(true), Is.True);
            root.Hero.SetPistolAim(true);
            root.Tick(.3f);
            for (int shot = 0; shot < 8 && !root.RoundFinished; shot++)
            {
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(.5f);
            }
            Assert.That(root.RoundFinished, Is.True);
            Assert.That(root.Hero.State.IsDefeated, Is.False);
            Assert.That(root.Opponent.State.Health, Is.Zero);
            root.Tick(2f);
            for (int frame = 0; frame < 24; frame++) yield return null;
            Assert.That(root.Hero.CombatFocused || root.CameraFollow.TargetLockActive, Is.False);
            AssertPistolLowCarry();
            yield return CapturePistolNearViews("idle-winner");

            int rounds = root.Hero.Pistol.Rounds;
            Assert.That(rounds, Is.LessThan(8));
            input.Press(keyboard.rKey, queueEventOnly: true);
            root.AutomaticSimulation = true;
            yield return null;
            input.Release(keyboard.rKey, queueEventOnly: true);
            yield return null;
            root.AutomaticSimulation = false;
            Assert.That(root.Hero.Pistol.IsReloading, Is.True, "R must still reload after victory.");
            int reloadSpawns = root.Projectiles.SpawnCount;
            Assert.That(root.Hero.RequestPistolShot(), Is.True);
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(reloadSpawns), "A winner cannot fire during reload.");
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(rounds));
            root.Tick(.9f);
            float progress = root.Hero.Pistol.ReloadProgress;
            Assert.That(progress, Is.GreaterThan(0f));
            yield return CapturePistolNearViews("winner-reload");
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(.8f);
            Assert.That(root.Hero.Pistol.ReloadProgress, Is.EqualTo(progress));
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release the pistol winner.");
            root.Tick(1.2f);
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8));

            root.AutomaticSimulation = true;
            input.Press(mouse.rightButton, queueEventOnly: true);
            yield return null;
            Assert.That(root.CameraFollow.FreeAimActive, Is.True, "The winner must retain mouse aiming.");
            float yaw = root.CameraFollow.Camera.transform.eulerAngles.y;
            input.Set(mouse.delta, new Vector2(80f, -10f), queueEventOnly: true);
            yield return null;
            Assert.That(Mathf.DeltaAngle(yaw, root.CameraFollow.Camera.transform.eulerAngles.y), Is.EqualTo(12.8f).Within(.1f));
            root.Tick(.6f);
            root.AutomaticSimulation = false;
            Assert.That(root.Hero.Pistol.IsAiming, Is.True);
            Assert.That(root.Hero.PistolSupportError, Is.LessThan(.02f), "The winner's left palm must support the held pistol.");
            yield return CapturePistolNearViews("winner-aim");
            root.AutomaticSimulation = true;
            int spawned = root.Projectiles.SpawnCount;
            input.Press(mouse.leftButton, queueEventOnly: true);
            yield return null;
            root.AutomaticSimulation = false;
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(spawned + 1));
            Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(1));
            Vector3 bullet = root.Projectiles.LastPosition;
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(1), "Finished-round ticks cannot clear a new bullet.");
            Assert.That(Vector3.Distance(bullet, root.Projectiles.LastPosition), Is.GreaterThan(1f));
            root.Tick(.15f);
            Assert.That(root.RoundFinished, Is.True);
            Assert.That(root.Opponent.State.Health, Is.Zero, "Free shooting cannot revive or restart the defeated opponent.");
            input.Release(mouse.leftButton, queueEventOnly: true);
            root.AutomaticSimulation = true;
            yield return null;
            yield return VerifyMouseAimAtNearbyFloor(input, mouse);
            root.AutomaticSimulation = true;
            input.Release(mouse.rightButton, queueEventOnly: true);
            yield return null;
            root.AutomaticSimulation = false;
            root.Tick(.4f);
            for (int frame = 0; frame < 24; frame++) yield return null;
            AssertPistolLowCarry();
            root.ResetRound();
            Assert.That(root.RoundFinished, Is.False);
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8));
            Assert.That(root.Projectiles.ActiveCount, Is.Zero);
        }

        private void AssertPistolLowCarry()
        {
            var visual = (Player3DCharacterPresentation)root.Player.Visual;
            visual.ReapplyLatePresentationPose();
            Assert.That(visual.OwnsClip(root.Hero), Is.False, "Ordinary gait keeps the winner's legs.");
            Assert.That(visual.OwnsCarryPose(root.Hero), Is.True);
            Transform muzzle = root.Hero.PistolMuzzle;
            Assert.That(Vector3.Dot(muzzle.forward, Vector3.up), Is.LessThan(-.85f), "Idle muzzle must point down.");
            Assert.That(Vector3.Dot(muzzle.right, root.Hero.transform.right), Is.GreaterThan(.95f), "The pistol must stay in its upright plane.");
            var hands = root.Hero.GetComponentInChildren<NpcHandPose>();
            Assert.That(Vector3.Distance(root.Hero.Weapon.transform.position, hands.CylinderCentre(false)), Is.LessThan(.001f));
            Assert.That(hands.LeftGripWeight, Is.Zero);
            AssertPistolWrist("Low pistol carry", 35f);
            Transform shoulder = CityPedestrianHandProps.FindSocket(root.Hero.DamageRigRoot, "upper_arm.R");
            Transform elbow = CityPedestrianHandProps.FindSocket(root.Hero.DamageRigRoot, "forearm.R");
            Assert.That(shoulder, Is.Not.Null);
            Assert.That(elbow, Is.Not.Null);
            Assert.That(Vector3.Angle(elbow.position - shoulder.position, -root.Hero.transform.up), Is.LessThan(45f),
                "Low carry must let the upper arm hang down rather than lift the elbow to shoulder height.");
        }

        [System.Serializable]
        private sealed class PistolJournalRecord
        {
            public string @event;
            public PistolJournalData data;
        }

        [System.Serializable]
        private sealed class PistolJournalData
        {
            public int actor;
            public int request;
            public int rounds;
            public string grip;
            public string reason;
            public float aim_error_degrees;
        }

        private static void AssertPistolJournalSnapshots(string folder, string session)
        {
            bool heroState = false, heroPresentation = false, opponentArm = false, opponentGrip = false;
            bool pistolState = false;
            var requests = new HashSet<int>();
            var terminals = new Dictionary<int, int>();
            var rejectionReasons = new HashSet<string>();
            int results = 0;
            foreach (string path in Directory.GetFiles(folder, "duel.ndjson", SearchOption.AllDirectories))
            {
                if (!Path.GetDirectoryName(path).Contains(session)) continue;
                foreach (string line in File.ReadAllLines(path))
                {
                    PistolJournalRecord record = JsonUtility.FromJson<PistolJournalRecord>(line);
                    if (record.@event == "round_result") results++;
                    if (record.data.actor == 1)
                    {
                        Assert.That(record.@event, Is.Not.EqualTo("arm_snapshot").And.Not.EqualTo("grip_snapshot"),
                            "A pistol has no crowbar support solver to measure.");
                        heroState |= record.@event == "state";
                        if (record.@event == "pistol_state")
                        {
                            pistolState = true;
                            Assert.That(record.data.rounds, Is.InRange(0, 8));
                        }
                        if (record.@event == "pistol_shot_requested")
                        {
                            Assert.That(record.data.request, Is.GreaterThan(0));
                            Assert.That(requests.Add(record.data.request), Is.True, "Every trigger needs its own diagnostic request.");
                        }
                        if (record.@event is "pistol_fired" or "pistol_shot_rejected")
                        {
                            Assert.That(record.data.request, Is.GreaterThan(0));
                            terminals.TryGetValue(record.data.request, out int count);
                            terminals[record.data.request] = count + 1;
                            if (record.@event == "pistol_shot_rejected")
                            {
                                Assert.That(record.data.reason, Is.Not.Null.And.Not.Empty);
                                rejectionReasons.Add(record.data.reason);
                            }
                            else Assert.That(record.data.aim_error_degrees, Is.LessThanOrEqualTo(CombatActor.MaximumPistolAimErrorDegrees));
                        }
                        if (record.@event == "presentation")
                        {
                            heroPresentation = true;
                            Assert.That(record.data.grip, Is.EqualTo("None"));
                        }
                    }
                    if (record.data.actor == 2)
                    {
                        opponentArm |= record.@event == "arm_snapshot";
                        opponentGrip |= record.@event == "grip_snapshot";
                    }
                }
            }
            Assert.That(heroState && heroPresentation && opponentArm && opponentGrip, Is.True,
                "The same journal must retain pistol state/pose and the opponent's crowbar measurements.");
            Assert.That(pistolState, Is.True, "Pistol snapshots must expose the state behind accepted and rejected triggers.");
            Assert.That(requests, Is.Not.Empty);
            CollectionAssert.AreEquivalent(requests, terminals.Keys, "Every requested trigger must finish with a shot or a rejection.");
            foreach (int request in requests)
                Assert.That(terminals[request], Is.EqualTo(1), $"Trigger {request} must have exactly one terminal outcome.");
            foreach (string reason in new[] { "raising", "cooldown", "reloading", "aim_unaligned", "muzzle_blocked" })
                Assert.That(rejectionReasons, Does.Contain(reason), "The deliberately rejected trigger must retain its exact reason.");
            Assert.That(results, Is.EqualTo(1), "Post-victory input cannot publish the duel result again.");
        }

        private IEnumerator AwaitSelectedCombatRange()
        {
            yield return WaitFor(() =>
            {
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                return root != null && root.IsInitialized && !SceneTransitionService.IsTransitioning &&
                    GameInput.CanRead(GameInputContext.Gameplay);
            }, "The selected combat loadout did not initialize.");
            root.AutomaticSimulation = false;
        }

        private void CapturePistolHandContact(Camera camera, string name)
        {
            ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
            var hands = root.Hero.GetComponentInChildren<NpcHandPose>();
            Transform grip = CombatPistolAssetProvider.FindAnchor(root.Hero.Weapon, "Grip");
            Assert.That(Vector3.Distance(grip.position, hands.CylinderCentre(false)), Is.LessThan(.001f));
            Assert.That(hands.RightGripWeight, Is.EqualTo(1f));
            foreach (NpcHandPose.HandBinding binding in hands.Hands)
                if (!binding.IsLeft)
                    foreach (SkinnedMeshRenderer skin in binding.Renderers)
                        Assert.That(skin.GetBlendShapeWeight(skin.sharedMesh.GetBlendShapeIndex(hands.ShapeName)), Is.EqualTo(100f));
                else if (root.Hero.Pistol.IsAiming)
                {
                    Assert.That(root.Hero.PistolSupportError, Is.LessThan(.02f), "The left hand must keep physical support at the pistol.");
                    Assert.That(hands.LeftGripWeight, Is.EqualTo(CombatPistolAssetProvider.SupportGripWeight));
                    foreach (SkinnedMeshRenderer skin in binding.Renderers)
                        Assert.That(skin.GetBlendShapeWeight(skin.sharedMesh.GetBlendShapeIndex(hands.ShapeName)),
                            Is.EqualTo(CombatPistolAssetProvider.SupportGripWeight * 100f));
                }
            if (root.Hero.Pistol.IsAiming && !root.Hero.Pistol.IsReloading)
            {
                AssertPistolWrist(name, 35f);
                AssertPistolWrist(name, 45f, true);
                AssertPistolHandsClear(name);
                AssertPistolArmsClearBody(name);
            }
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, name + ".png");
            LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
            AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
            Assert.That(Vector3.Distance(grip.position, hands.CylinderCentre(false)), Is.LessThan(.001f));
        }

        private IEnumerator CapturePistolNearViews(string action)
        {
            Camera camera = root.CameraFollow.Camera;
            Transform actor = root.Hero.transform;
            Vector3 originalPosition = camera.transform.position;
            Quaternion originalRotation = camera.transform.rotation;
            float originalFieldOfView = camera.fieldOfView;
            bool followEnabled = root.CameraFollow.enabled;
            Vector3 target = root.Hero.Ragdoll.PhysicsController.ChestBody.position + actor.forward * .15f + actor.up * .06f;
            try
            {
                root.CameraFollow.enabled = false;
                camera.fieldOfView = 45f;
                Vector3 front = actor.position + actor.forward * 1.9f + actor.up * 1.45f;
                camera.transform.SetPositionAndRotation(front, Quaternion.LookRotation(target - front, actor.up));
                yield return null;
                CapturePistolHandContact(camera, "pistol-" + action + "-front");
                Vector3 side = actor.position + actor.right * 1.7f + actor.forward * .25f + actor.up * 1.45f;
                camera.transform.SetPositionAndRotation(side, Quaternion.LookRotation(target - side, actor.up));
                yield return null;
                CapturePistolHandContact(camera, "pistol-" + action + "-side");
                if (root.Hero.Pistol.IsAiming && !root.Hero.Pistol.IsReloading)
                {
                    Vector3 hands = CombatPistolAssetProvider.FindAnchor(root.Hero.Weapon, "Grip").position;
                    Vector3 eye = hands + actor.forward * .6f + actor.right * .28f + actor.up * .16f;
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(hands - eye, actor.up));
                    yield return null;
                    CapturePistolHandContact(camera, "pistol-" + action + "-hands");
                }
            }
            finally
            {
                root.CameraFollow.enabled = followEnabled;
                camera.fieldOfView = originalFieldOfView;
                camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
            }
        }
    }
}
