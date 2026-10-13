using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using static BarPromenade.Tests.PlayMode.CombatTuning;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_CrowbarAttackAndBlockUseFacingWithoutTargetFocus()
        {
            var input = new InputTestFixture();
            Mouse mouse = null;
            try
            {
                input.Setup();
                mouse = InputSystem.AddDevice<Mouse>();
                input.Set(mouse.position, new Vector2(Screen.width * .75f, Screen.height * .5f), queueEventOnly: true);
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return EnterRange();
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                PlacePair(4f);
                root.Hero.ResetActor(root.Hero.transform.position, Vector3.right);
                Assert.That(root.SetOpponentFocus(false), Is.True);
                AssertFocusState(false);
                var presentation = (Player3DCharacterPresentation)root.Player.Visual;
                root.AutomaticSimulation = true;
                for (int frame = 0; frame < 4; frame++) yield return null;

                Quaternion facing = root.Hero.transform.rotation;
                int beforeSwing = root.Hero.State.AttackSequence;
                input.Press(mouse.leftButton, queueEventOnly: true);
                for (int frame = 0; frame < 2; frame++) yield return null;
                Assert.That(root.Hero.State.IsCharging, Is.True, "LMB works without a target lock.");
                Assert.That(root.ChargeMeterVisible, Is.True);
                Assert.That(presentation.OwnsClip(root.Hero), Is.True, "The free swing claims the same visible hero rig.");
                AssertFocusState(false);
                input.Release(mouse.leftButton, queueEventOnly: true);
                for (int frame = 0; frame < 2; frame++) yield return null;
                Assert.That(root.Hero.State.IsAttacking, Is.True, "Releasing LMB commits the forward swing.");
                Assert.That(root.Hero.State.AttackSequence, Is.GreaterThan(beforeSwing));
                for (int frame = 0; frame < 120 && root.Hero.State.Phase != MeleePhase.Ready; frame++)
                {
                    Assert.That(Quaternion.Angle(facing, root.Hero.transform.rotation), Is.LessThan(.1f),
                        "An unfocused strike keeps the hero's facing instead of turning toward the selected opponent.");
                    yield return null;
                }
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
                Assert.That(root.Opponent.ReceivedImpactCount, Is.Zero, "A target outside the forward arc takes no damage.");
                Assert.That(presentation.OwnsClip(root.Hero), Is.False, "The finished free strike returns to ordinary movement.");
                AssertFocusState(false);

                input.Press(mouse.rightButton, queueEventOnly: true);
                for (int frame = 0; frame < 40 && !root.Hero.State.IsBlocking; frame++) yield return null;
                Assert.That(root.Hero.State.IsBlocking, Is.True, "RMB raises the two-hand block without a target lock.");
                Assert.That(presentation.OwnsClip(root.Hero), Is.True);
                Assert.That(Quaternion.Angle(facing, root.Hero.transform.rotation), Is.LessThan(.1f));
                AssertFocusState(false);
                input.Release(mouse.rightButton, queueEventOnly: true);
                for (int frame = 0; frame < 4; frame++) yield return null;
                Assert.That(root.Hero.State.IsBlocking || root.Hero.GuardRequested, Is.False);
                Assert.That(presentation.OwnsClip(root.Hero), Is.False);

                Assert.That(root.PauseMenu.Open(), Is.True);
                yield return null;
                Assert.That(root.Hero.TryAttack() || root.Hero.RequestCharge(), Is.False,
                    "Removing target focus does not bypass the modal input owner.");
                input.Press(mouse.leftButton, queueEventOnly: true);
                for (int frame = 0; frame < 2; frame++) yield return null;
                Assert.That(root.Hero.State.IsCharging || root.Hero.State.IsAttacking, Is.False);
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release free melee input.");
                for (int frame = 0; frame < 2; frame++) yield return null;
                Assert.That(root.Hero.State.IsCharging || root.Hero.State.IsAttacking, Is.False,
                    "An attack button held through pause must wait for release and a new press.");
                input.Release(mouse.leftButton, queueEventOnly: true);
                for (int frame = 0; frame < 2; frame++) yield return null;

                // Direct commands share the input bridge's optional-focus contract.
                root.AutomaticSimulation = false;
                PlacePair(.82f);
                Assert.That(root.SetOpponentFocus(false), Is.True);
                Assert.That(root.Hero.TryAttack(), Is.True);
                Assert.That(root.Hero.State.IsAttacking, Is.True);
                Assert.That(root.Hero.State.IsShoving, Is.False, "Free melee remains a crowbar swing at close range.");

                PlacePair(1.1f);
                Assert.That(root.SetOpponentFocus(false), Is.True);
                Assert.That(root.Hero.RequestCharge(), Is.True);
                root.Hero.Step(.1f);
                Assert.That(root.Hero.ReleaseCharge(), Is.True);
                root.Hero.Step(2f);
                Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(1),
                    "The same actual crowbar collision can hit a body ahead without a focus marker.");
                Assert.That(root.Opponent.LastImpact.Damage, Is.GreaterThan(0f));
                AssertFocusState(false);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                input.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator Range_MiddleMouseToggleRestoresWalkingWithoutEndingHostility()
        {
            var input = new InputTestFixture();
            Mouse mouse = null;
            Keyboard keyboard = null;
            GameObject obstruction = null;
            try
            {
                input.Setup();
                mouse = InputSystem.AddDevice<Mouse>();
                keyboard = InputSystem.AddDevice<Keyboard>();
                input.Set(mouse.position, new Vector2(Screen.width * .75f, Screen.height * .5f), queueEventOnly: true);
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return EnterRange();
                AssertFocusState(true);
                Assert.That(root.Sparring, Is.True);
                root.CameraFollow.Snap();
                Assert.That(root.TryGetFocusMarkerScreenPosition(out Vector2 marker), Is.True);
                Assert.That(marker.x, Is.InRange(0f, (float)Screen.width));
                Assert.That(marker.y, Is.InRange(0f, (float)Screen.height));
                Vector3 projectedChest = root.CameraFollow.Camera.WorldToScreenPoint(
                    root.Opponent.Ragdoll.PhysicsController.ChestBody.position);
                Assert.That(Vector2.Distance(marker, new Vector2(projectedChest.x, projectedChest.y)), Is.LessThan(1f),
                    "The point stays attached to the live chest rather than the NPC's floor root.");
                Assert.That(root.FocusMarkerVisible, Is.True);
                yield return CaptureFocusGameView("focus-01-locked");

                Camera camera = root.CameraFollow.Camera;
                Vector3 chest = root.Opponent.Ragdoll.PhysicsController.ChestBody.position;
                obstruction = new GameObject("Test focus marker obstruction");
                obstruction.transform.SetParent(root.transform, false);
                obstruction.transform.position = Vector3.Lerp(camera.transform.position, chest, .7f);
                BoxCollider cover = obstruction.AddComponent<BoxCollider>();
                cover.size = Vector3.one * 1.2f;
                Physics.SyncTransforms();
                Assert.That(root.FocusMarkerVisible, Is.False, "A solid obstruction hides the point on the opponent.");
                cover.enabled = false;
                Object.Destroy(obstruction);
                obstruction = null;
                Quaternion cameraRotation = camera.transform.rotation;
                camera.transform.rotation *= Quaternion.Euler(0f, 180f, 0f);
                Assert.That(root.FocusMarkerVisible, Is.False, "The marker cannot float on screen behind the camera.");
                camera.transform.rotation = cameraRotation;

                root.AutomaticSimulation = true;
                // Resume the input owner after entry before pressing a gameplay button.
                yield return null;
                input.Press(mouse.leftButton, queueEventOnly: true);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(root.Hero.State.IsCharging, Is.True,
                    $"Live LMB must charge: phase={root.Hero.State.Phase}, available={root.Hero.IsAvailable}, input={GameInput.CanRead(GameInputContext.Gameplay)}.");
                input.Press(mouse.middleButton, queueEventOnly: true);
                yield return null;
                AssertFocusState(false);
                Assert.That(root.Hero.State.IsCharging || root.Hero.State.HasBufferedCharge, Is.False);
                int heroSequence = root.Hero.State.AttackSequence; // Cancellation invalidates the old charge's sequence.
                Assert.That(root.FocusMarkerVisible, Is.False);
                for (int frame = 0; frame < 8; frame++) yield return null;
                AssertFocusState(false); // Holding MMB must not toggle again.
                input.Release(mouse.middleButton, queueEventOnly: true);
                yield return null;
                Assert.That(root.Hero.TryKick(), Is.False);
                Assert.That(root.Hero.TryStep(Vector2.down), Is.False);
                root.Hero.SetBlock(true);
                Assert.That(root.Hero.GuardRequested, Is.True, "The basic guard remains available without focus.");
                root.Hero.SetBlock(false);

                input.Press(mouse.middleButton, queueEventOnly: true);
                yield return null;
                AssertFocusState(true);
                input.Release(mouse.middleButton, queueEventOnly: true);
                for (int frame = 0; frame < 8; frame++) yield return null;
                Assert.That(root.Hero.State.IsCharging || root.Hero.State.HasBufferedCharge || root.Hero.State.IsAttacking,
                    Is.False, "A held LMB cancelled by focus loss must wait for a fresh press after reacquiring.");
                Assert.That(root.Hero.State.AttackSequence, Is.EqualTo(heroSequence));
                input.Press(mouse.middleButton, queueEventOnly: true);
                yield return null;
                AssertFocusState(false);
                input.Release(mouse.middleButton, queueEventOnly: true);
                // Mouse buttons share a state byte: flush this release before queuing another button.
                yield return null;
                input.Release(mouse.leftButton, queueEventOnly: true);
                for (int frame = 0; frame < 24; frame++) yield return null;
                AssertFocusState(false);

                // Isolate locomotion from incoming reactions without resetting either fighter or the AI.
                root.AutomaticSimulation = false;
                var presentation = (Player3DCharacterPresentation)root.Player.Visual;
                input.Press(keyboard.sKey, queueEventOnly: true);
                for (int frame = 0; frame < 8; frame++) yield return null;
                Vector3 start = root.Hero.transform.position;
                Vector3 forward = root.Hero.transform.forward;
                Transform pelvis = presentation.Registry.Anchors.Pelvis;
                Vector3 pelvisStart = pelvis.position;
                var walk = new WalkingLegProbe(root.Hero);
                for (int frame = 0; frame < 24; frame++) { yield return null; walk.Sample(); }
                Assert.That(Vector3.Dot(root.Hero.transform.position - start, forward), Is.LessThan(-.25f),
                    WalkingDiagnostic("S without focus", start));
                Assert.That(Vector3.Dot(pelvis.position - pelvisStart, forward), Is.LessThan(-.2f));
                Assert.That(presentation.CurrentLocomotionState, Is.EqualTo(Player3DLocomotionState.WalkBack));
                Assert.That(presentation.OwnsClip(root.Hero), Is.False, "Ordinary locomotion owns the whole visible rig.");
                walk.AssertMoving(2f, "Both legs must walk after leaving focus.");
                input.Release(keyboard.sKey, queueEventOnly: true);
                for (int frame = 0; frame < 12; frame++) yield return null;
                Quaternion facing = root.Hero.transform.rotation;
                input.Press(keyboard.dKey, queueEventOnly: true);
                for (int frame = 0; frame < 12; frame++) yield return null;
                input.Release(keyboard.dKey, queueEventOnly: true);
                yield return null;
                Assert.That(Quaternion.Angle(facing, root.Hero.transform.rotation), Is.GreaterThan(20f),
                    "D resumes ordinary turning instead of facing the opponent.");
                yield return CaptureFocusGameView("focus-02-free");

                int opponentRound = root.OpponentRound;
                int opponentSequence = root.Opponent.State.AttackSequence;
                float health = root.Hero.State.Health;
                root.AutomaticSimulation = true;
                yield return WaitFor(() => root.Hero.State.Health < health,
                    "The active NPC must pursue and land a real weapon contact while the hero is unfocused.");
                root.AutomaticSimulation = false;
                AssertFocusState(false);
                Assert.That(root.RoundFinished, Is.False);
                Assert.That(root.Sparring, Is.True);
                Assert.That(root.Opponent.State.AttackSequence, Is.GreaterThan(opponentSequence));
                Assert.That(root.Hero.ReceivedImpactCount, Is.GreaterThan(0));
                Assert.That(root.Hero.LastImpact.Damage, Is.GreaterThan(0f));
                float injuredHealth = root.Hero.State.Health;
                float heroStamina = root.Hero.State.Stamina;
                float opponentHealth = root.Opponent.State.Health;
                float opponentStamina = root.Opponent.State.Stamina;
                Vector3 heroPosition = root.Hero.transform.position;
                Vector3 opponentPosition = root.Opponent.transform.position;
                int decisions = root.OpponentDecisionSequence;
                Assert.That(root.SetOpponentFocus(true), Is.True);
                AssertFocusState(true);
                Assert.That(root.Hero.State.Health, Is.EqualTo(injuredHealth));
                Assert.That(root.Hero.State.Stamina, Is.EqualTo(heroStamina));
                Assert.That(root.Opponent.State.Health, Is.EqualTo(opponentHealth));
                Assert.That(root.Opponent.State.Stamina, Is.EqualTo(opponentStamina));
                Assert.That(root.Hero.transform.position, Is.EqualTo(heroPosition));
                Assert.That(root.Opponent.transform.position, Is.EqualTo(opponentPosition));
                Assert.That(root.OpponentRound, Is.EqualTo(opponentRound));
                Assert.That(root.OpponentDecisionSequence, Is.EqualTo(decisions), "Acquiring focus cannot reset the NPC's mind.");

                // Advance only the injured hero so another hostile swing cannot conceal the recovery handoff.
                Assert.That(root.SetOpponentFocus(false), Is.True);
                root.Hero.SetPresentationFrozen(false);
                float recoveryDeadline = Time.realtimeSinceStartup + 10f;
                while ((root.Hero.State.Phase != MeleePhase.Ready || root.Hero.IsKnockedDown ||
                    root.Hero.IsRagdollActive || presentation.OwnsClip(root.Hero)) &&
                    Time.realtimeSinceStartup < recoveryDeadline)
                {
                    root.Hero.Step(1f / 60f);
                    yield return null;
                }
                AssertFocusState(false);
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
                Assert.That(root.Hero.IsKnockedDown || root.Hero.IsRagdollActive, Is.False);
                Assert.That(presentation.OwnsClip(root.Hero), Is.False,
                    "Finishing an injury returns the unfocused hero to ordinary locomotion.");
                input.Press(keyboard.sKey, queueEventOnly: true);
                for (int frame = 0; frame < 8; frame++) yield return null;
                start = root.Hero.transform.position;
                forward = root.Hero.transform.forward;
                for (int frame = 0; frame < 12; frame++) yield return null;
                Assert.That(Vector3.Dot(root.Hero.transform.position - start, forward), Is.LessThan(-.12f),
                    WalkingDiagnostic("S after unfocused injury", start));
                input.Release(keyboard.sKey, queueEventOnly: true);
                yield return null;
                Assert.That(root.Hero.State.Health, Is.EqualTo(injuredHealth));
                Assert.That(root.SetOpponentFocus(true), Is.True);

                root.AutomaticSimulation = true;
                Assert.That(root.PauseMenu.Open(), Is.True);
                yield return null;
                Assert.That(root.FocusMarkerVisible, Is.False);
                Assert.That(root.SetOpponentFocus(false), Is.False, "A modal owner rejects direct focus commands too.");
                input.Press(mouse.middleButton, queueEventOnly: true);
                for (int frame = 0; frame < 3; frame++) yield return null;
                AssertFocusState(true);
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release focus input.");
                for (int frame = 0; frame < 2; frame++) yield return null;
                AssertFocusState(true); // A press consumed by pause must not replay when still held.
                input.Release(mouse.middleButton, queueEventOnly: true);
                yield return null;
                Assert.That(root.SetOpponentFocus(false), Is.True);
                root.ResetRound();
                yield return null;
                yield return null;
                AssertFocusState(true);
                Assert.That(root.Hero.State.Health, Is.EqualTo(S.MaxHealth));
                Assert.That(root.OpponentRound, Is.EqualTo(opponentRound + 1));

                // Focus loss cancels future input, but a paid swing keeps its own clock and sequence.
                root.AutomaticSimulation = false;
                PlacePair(4f);
                yield return null;
                yield return null;
                Assert.That(root.Hero.TryAttack(), Is.True);
                root.Tick(.05f);
                float elapsed = root.Hero.State.AttackElapsed;
                int committed = root.Hero.State.AttackSequence;
                Assert.That(root.SetOpponentFocus(false), Is.True);
                Assert.That(root.Hero.State.AttackElapsed, Is.EqualTo(elapsed));
                root.Tick(CombatTestRoot.SimulationStep * 2f);
                Assert.That(root.Hero.State.AttackElapsed, Is.GreaterThan(elapsed));
                Assert.That(root.Hero.State.AttackSequence, Is.EqualTo(committed));
                root.Tick(2f);
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
                Assert.That(root.Hero.State.AttackSequence, Is.EqualTo(committed));
                Assert.That(presentation.OwnsClip(root.Hero), Is.False);
                PlayerCameraFollow oldFollow = root.CameraFollow;
                CombatActor oldHero = root.Hero;
                Assert.That(root.ReturnToMenu(), Is.True);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                    !SceneTransitionService.IsTransitioning, "The focus fixture could not return to the menu.");
                Assert.That(oldFollow == null && oldHero == null, Is.True, "Scene exit removes focus and presentation owners.");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                if (obstruction != null) Object.Destroy(obstruction);
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        private void AssertFocusState(bool focused)
        {
            Assert.That(root.IsOpponentFocused, Is.EqualTo(focused));
            Assert.That(root.Hero.CombatFocused, Is.EqualTo(focused));
            Assert.That(root.CameraFollow.TargetLockActive, Is.EqualTo(focused));
            Assert.That(root.Player.Motor.MovementTargetActive, Is.EqualTo(focused));
            Assert.That(root.Opponent.CombatFocused, Is.True, "The NPC never leaves combat when the hero changes focus.");
        }

        private IEnumerator CaptureFocusGameView(string shot)
        {
            // Batchmode has no Game view to capture; projection assertions above cover its UI contract.
            if (Application.isBatchMode)
            {
                string capturePath = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, shot + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + capturePath);
                AreaCaptureFixture.CaptureCurrentCamera(root.CameraFollow.Camera, SceneIds.CombatTest, shot);
                yield break;
            }
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", SceneIds.CombatTest, shot + ".png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            DateTime oldWrite = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 3f;
            do { yield return null; }
            while ((!File.Exists(path) || File.GetLastWriteTimeUtc(path) <= oldWrite) && Time.realtimeSinceStartup < deadline);
            Assert.That(File.Exists(path) && File.GetLastWriteTimeUtc(path) > oldWrite, Is.True,
                "The focused/free Game view must include the actual IMGUI point and locomotion pose.");
        }
    }
}
