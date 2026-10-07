using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using static BarPromenade.Tests.PlayMode.CombatTuning;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_ReleasedWalkStopsAfterCatchAndLostApplicationFocus()
        {
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            Gamepad gamepad = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                gamepad = InputSystem.AddDevice<Gamepad>();
                GameInput.HandleApplicationFocus(true);
                GameInput.ReadMovement();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
                yield return EnterRange();
                foreach (bool loseApplicationFocus in new[] { false, true })
                {
                    PlacePair(4f);
                    bool focused = !loseApplicationFocus;
                    if (root.IsOpponentFocused != focused)
                        Assert.That(root.SetOpponentFocus(focused), Is.True);
                    GameInput.HandleApplicationFocus(true);
                    GameInput.ReadMovement();
                    yield return BeginWalkingForRelease(input, keyboard);

                    CombatActor hero = root.Hero;
                    CombatActor opponent = root.Opponent;
                    float health = hero.State.Health;
                    // Move only the stationary partner into palm range. The
                    // player's speed and the subsequent contact remain real.
                    Vector3 partner = hero.transform.position + hero.transform.forward * .82f;
                    opponent.Body.Move(partner - opponent.transform.position);
                    opponent.Present();
                    Physics.SyncTransforms();
                    Assert.That(opponent.RequestAttack(), Is.True);
                    Assert.That(opponent.State.IsShoving, Is.True);
                    for (int frame = 0; frame < 120 && !hero.Footwork.CatchStepActive; frame++)
                    { root.Tick(ImpactFrameSeconds); yield return null; }
                    Assert.That(hero.Footwork.CatchStepActive, Is.True,
                        "A real palm contact must start the catch before input is withdrawn. " +
                        DescribeImpactRecoveryGate(hero));
                    Assert.That(hero.ReceivedImpactCount, Is.EqualTo(1));
                    Assert.That(hero.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Shove));
                    Assert.That(hero.LastImpact.Impulse.magnitude, Is.EqualTo(165f).Within(.01f));

                    Vector3 withdrawalPosition = hero.transform.position;
                    if (loseApplicationFocus)
                    {
                        // This is the production Application.focusChanged
                        // handler. Deliberately omit key-up to retain stale W.
                        GameInput.HandleApplicationFocus(false);
                        Assert.That(keyboard.wKey.isPressed, Is.True);
                    }
                    else
                    {
                        input.Release(keyboard.wKey, queueEventOnly: true);
                        InputSystem.Update();
                        Assert.That(keyboard.wKey.isPressed, Is.False);
                    }
                    Assert.That(GameInput.ReadMovement(), Is.EqualTo(Vector2.zero));
                    Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.True,
                        "Withdrawing walking input must leave physical recovery running.");
                    Assert.That(GameInput.CanRead(GameInputContext.Movement), Is.True);
                    for (int frame = 0; frame < 180 && (hero.Footwork.CatchStepActive ||
                        !hero.HasAttackBalance || hero.State.Phase != MeleePhase.Ready); frame++)
                    {
                        root.Tick(ImpactFrameSeconds);
                        yield return null;
                        Assert.That(hero.IsKnockedDown, Is.False, DescribeImpactRecoveryGate(hero));
                    }
                    Assert.That(hero.HasAttackBalance && !hero.Footwork.CatchStepActive, Is.True,
                        "Both finite catch landings must finish without another command. " +
                        DescribeImpactRecoveryGate(hero));
                    Assert.That(hero.Footwork.CatchStepCount, Is.EqualTo(2));
                    Assert.That(Vector3.Distance(hero.transform.position, withdrawalPosition), Is.GreaterThan(.02f),
                        "The existing shove must still carry the body while walking input is zero.");
                    yield return VerifyWalkingSettlesWithoutInput(keyboard, "after real shove");
                    Assert.That(hero.State.Health, Is.EqualTo(health));

                    if (!loseApplicationFocus) continue;
                    Assert.That(GameInput.MovementFocused, Is.False);
                    GameInput.HandleApplicationFocus(true);
                    Assert.That(GameInput.MovementAwaitingNeutral, Is.True);
                    Vector3 stopped = hero.transform.position;
                    for (int frame = 0; frame < 30; frame++)
                    {
                        root.Tick(ImpactFrameSeconds);
                        yield return null;
                        Assert.That(keyboard.wKey.isPressed, Is.True,
                            "No key-up has been delivered while the window lost focus.");
                        Assert.That(GameInput.ReadMovement(), Is.EqualTo(Vector2.zero));
                        Assert.That(root.Player.Motor.PlanarVelocity.magnitude, Is.LessThan(.02f));
                    }
                    Assert.That(Vector3.Distance(hero.transform.position, stopped), Is.LessThan(.02f),
                        "Returning to the window cannot revive a stale walking key.");
                    input.Press(keyboard.sKey, queueEventOnly: true);
                    InputSystem.Update();
                    Assert.That(keyboard.wKey.isPressed && keyboard.sKey.isPressed, Is.True);
                    Assert.That(GameInput.ReadMovement(), Is.EqualTo(Vector2.zero));
                    Assert.That(GameInput.MovementAwaitingNeutral, Is.True,
                        "Opposing held keys cancel their vector but are not neutral input.");
                    input.Set(gamepad.leftStick, new Vector2(.4f, 0f), queueEventOnly: true);
                    // W and S share a state byte; release them in one event.
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    InputSystem.Update();
                    Assert.That(keyboard.wKey.isPressed || keyboard.aKey.isPressed ||
                        keyboard.sKey.isPressed || keyboard.dKey.isPressed, Is.False);
                    Assert.That(gamepad.leftStick.ReadValue().sqrMagnitude, Is.GreaterThan(.01f));
                    Assert.That(GameInput.ReadMovement(), Is.EqualTo(Vector2.zero));
                    Assert.That(GameInput.MovementAwaitingNeutral, Is.True,
                        "Releasing the keyboard cannot rearm movement while a stick remains deflected.");
                    input.Set(gamepad.leftStick, Vector2.zero, queueEventOnly: true);
                    InputSystem.Update();
                    Assert.That(GameInput.ReadMovement(), Is.EqualTo(Vector2.zero));
                    Assert.That(GameInput.MovementAwaitingNeutral, Is.False);
                    yield return BeginWalkingForRelease(input, keyboard);
                    input.Release(keyboard.wKey, queueEventOnly: true);
                    InputSystem.Update();
                    yield return VerifyWalkingSettlesWithoutInput(keyboard, "after a fresh walking press");
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                if (gamepad != null && gamepad.added) InputSystem.RemoveDevice(gamepad);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                GameInput.HandleApplicationFocus(true);
                GameInput.ReadMovement();
                input.TearDown();
            }
        }

        private IEnumerator BeginWalkingForRelease(InputTestFixture input, Keyboard keyboard)
        {
            Vector3 start = root.Hero.transform.position;
            input.Press(keyboard.wKey, queueEventOnly: true);
            for (int frame = 0; frame < 60 && root.Player.Motor.PlanarVelocity.magnitude < 1.2f; frame++)
            { root.Tick(ImpactFrameSeconds); yield return null; }
            Assert.That(GameInput.ReadMovement().y, Is.EqualTo(1f));
            Assert.That(root.Player.Motor.PlanarVelocity.magnitude, Is.GreaterThanOrEqualTo(1.2f),
                WalkingDiagnostic("W before release", start));
            Assert.That(Vector3.Distance(root.Hero.transform.position, start), Is.GreaterThan(.08f),
                "The fixture starts with achieved player walking, not a fabricated velocity.");
        }

        private IEnumerator VerifyWalkingSettlesWithoutInput(Keyboard keyboard, string stage)
        {
            CombatActor hero = root.Hero;
            float previous = root.Player.Motor.PlanarVelocity.magnitude;
            int brakingFrames = Mathf.CeilToInt((previous / 11f + .1f) / ImpactFrameSeconds);
            int landings = hero.ImpactMotion.LandedRecoverySteps, recovery = hero.ImpactMotion.RecoverySequence;
            int impacts = hero.ReceivedImpactCount;
            Vector3 stopped = hero.transform.position;
            for (int frame = 0; frame < brakingFrames + 90; frame++)
            {
                root.Tick(ImpactFrameSeconds);
                yield return null;
                float speed = root.Player.Motor.PlanarVelocity.magnitude;
                string context = $"{stage}, frame={frame}, raw W={keyboard.wKey.isPressed}, " +
                    $"input={GameInput.ReadMovement()}, speed={speed:F4}, previous={previous:F4}, " +
                    $"requested={root.Player.Motor.RequestedPlanarVelocity}, " +
                    $"drift={root.Player.Motor.BalanceDriftVelocity}, " +
                    DescribeImpactRecoveryGate(hero);
                Assert.That(GameInput.ReadMovement(), Is.EqualTo(Vector2.zero), context);
                Assert.That(root.Player.Motor.RequestedPlanarVelocity.sqrMagnitude, Is.LessThan(.000001f), context);
                Assert.That(speed, Is.LessThanOrEqualTo(previous + .03f),
                    "A released walk must not accelerate again after balance returns. " + context);
                if (frame >= brakingFrames)
                    Assert.That(speed, Is.LessThan(.02f), "Walking must exhaust its bounded braking tail. " + context);
                Assert.That(hero.ImpactMotion.LandedRecoverySteps, Is.EqualTo(landings), context);
                Assert.That(hero.Footwork.CatchStepActive || hero.Footwork.RecoveryEpisodeActive, Is.False, context);
                Assert.That(hero.ImpactMotion.RecoverySequence, Is.EqualTo(recovery), context);
                Assert.That(hero.ReceivedImpactCount, Is.EqualTo(impacts), context);
                if (frame == brakingFrames + 29) stopped = hero.transform.position;
                previous = speed;
            }
            Assert.That(Vector3.Distance(hero.transform.position, stopped), Is.LessThan(.02f),
                "The stopped root must remain still for another second without input. " + stage);
            Assert.That(hero.ImpactMotion.MovementLandingActive, Is.False);
        }

        [UnityTest]
        public IEnumerator Range_LivingRecoveryEscapesBlockedWeaponAndSupportsUnarmedActions()
        {
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            GameObject cage = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
                yield return EnterRange();
                CombatActor hero = root.Hero;
                CombatActor opponent = root.Opponent;
                GameObject originalWeapon = hero.Weapon;
                int round = root.OpponentRound;
                float health = hero.State.Health;
                Assert.That(root.Sparring, Is.True);
                Assert.That(root.SetOpponentFocus(false), Is.True);
                yield return null;
                yield return null;

                // Use the real physical fall. Only the hero is manually advanced
                // during setup, so a random repeat hit cannot hide a stuck rise.
                ApplyControlledImpact(hero, -hero.transform.forward, ChargedDiagnosticImpulse);
                bool sawPhysics = false;
                for (int frame = 0; frame < 720 && !hero.Ragdoll.IsRecovering; frame++)
                {
                    hero.Step(ImpactFrameSeconds);
                    sawPhysics |= hero.Ragdoll.PhysicsController.IsSimulating;
                    yield return null;
                }
                Assert.That(sawPhysics && hero.IsKnockedDown && hero.Ragdoll.IsRecovering, Is.True,
                    "The regression starts from a living body that actually fell. " + DescribeImpactRecoveryGate(hero));

                VerifyRecoveryFloorContactPaths(hero);

                cage = CreateRecoveryWeaponCage(hero);
                Physics.SyncTransforms();
                bool sawArmEscape = false, checkedSuspension = false;
                float greatestBlockedSeconds = 0f;
                for (int frame = 0; frame < 180 && !hero.IsWeaponDropped; frame++)
                {
                    hero.Step(ImpactFrameSeconds);
                    yield return null;
                    greatestBlockedSeconds = Mathf.Max(greatestBlockedSeconds, hero.RecoveryBlockedSeconds);
                    sawArmEscape |= hero.RecoveryEscapeStage == "arm";
                    if (!checkedSuspension && hero.RecoveryBlockedSeconds > .1f)
                    {
                        checkedSuspension = true;
                        float blocked = hero.RecoveryBlockedSeconds;
                        Assert.That(root.PauseMenu.Open(), Is.True);
                        yield return null;
                        for (int pause = 0; pause < 3; pause++) { hero.Step(.5f); yield return null; }
                        Assert.That(hero.RecoveryBlockedSeconds, Is.EqualTo(blocked).Within(.0001f),
                            "Pause is not time spent trying to free the weapon.");
                        Assert.That(root.PauseMenu.Cancel(), Is.True);
                        yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Recovery pause did not close.");
                        hero.SetPresentationFrozen(true);
                        for (int freeze = 0; freeze < 3; freeze++) { hero.Step(.5f); yield return null; }
                        Assert.That(hero.RecoveryBlockedSeconds, Is.EqualTo(blocked).Within(.0001f),
                            "Hit-stop cannot spend the unjamming budget.");
                        hero.SetPresentationFrozen(false);
                    }
                    Assert.That(hero.State.Health, Is.EqualTo(health));
                    Assert.That(hero.CombatFocused, Is.False);
                    Assert.That(root.RoundFinished, Is.False);
                    Assert.That(root.OpponentRound, Is.EqualTo(round));
                }
                Assert.That(checkedSuspension && sawArmEscape && hero.IsWeaponDropped, Is.True,
                    "A physically trapped bar must try the arm route, then release within a bounded budget. " +
                    DescribeImpactRecoveryGate(hero));
                Assert.That(greatestBlockedSeconds, Is.LessThan(1.5f));
                Assert.That(hero.Weapon, Is.SameAs(originalWeapon), "Release keeps the original authored object.");
                Assert.That(hero.Weapon.transform.IsChildOf(hero.transform), Is.False);
                Assert.That(hero.Weapon.GetComponent<Rigidbody>().isKinematic, Is.False);
                Assert.That(hero.State.IsDefeated, Is.False);
                cage.SetActive(false);
                Object.Destroy(cage);
                cage = null;
                yield return null;

                VerifyReleasedWeaponStandingClearance(hero);

                yield return FinishIsolatedRecovery(hero, "The body must rise without waiting for a missing weapon.");
                Assert.That(hero.State.Health, Is.EqualTo(health));
                Assert.That(hero.IsWeaponDropped, Is.True);
                Assert.That(hero.CombatFocused, Is.False);
                Assert.That(root.Sparring && opponent.CombatFocused, Is.True);

                input.Press(keyboard.sKey, queueEventOnly: true);
                for (int frame = 0; frame < 8; frame++) yield return null;
                Vector3 walkingStart = hero.transform.position;
                Vector3 facing = hero.transform.forward;
                for (int frame = 0; frame < 12; frame++) yield return null;
                Assert.That(Vector3.Dot(hero.transform.position - walkingStart, facing), Is.LessThan(-.12f),
                    "Unfocused recovery returns ordinary walking without equipping the bar.");
                input.Release(keyboard.sKey, queueEventOnly: true);
                yield return null;
                Assert.That(root.SetOpponentFocus(true), Is.True);
                for (int frame = 0; frame < 12; frame++) { hero.Step(ImpactFrameSeconds); yield return null; }
                int sequence = hero.State.AttackSequence;
                Assert.That(hero.TryAttack(), Is.False);
                Assert.That(hero.RequestAttack(), Is.False);
                Assert.That(hero.RequestCharge(), Is.False);
                Assert.That(hero.ReleaseCharge(), Is.False);
                hero.SetBlock(true);
                Assert.That(hero.State.IsBlocking || hero.State.IsCharging || hero.State.IsShoving, Is.False);
                Assert.That(hero.State.AttackSequence, Is.EqualTo(sequence));

                // The close-range attack route must not create a two-handed shove.
                Vector3 opponentStart = opponent.transform.position;
                opponent.Body.Move(hero.transform.position + hero.transform.forward * .65f - opponent.transform.position);
                Physics.SyncTransforms();
                Assert.That(hero.RequestAttack(), Is.False);
                Assert.That(hero.State.IsShoving, Is.False);
                opponent.Body.Move(opponentStart - opponent.transform.position);
                Physics.SyncTransforms();
                Assert.That(hero.TryKick(), Is.True, "Losing the crowbar leaves grounded kicks available.");
                bool sawKick = hero.State.IsKicking;
                for (int frame = 0; frame < 180; frame++)
                {
                    hero.Step(ImpactFrameSeconds);
                    yield return null;
                    sawKick |= hero.State.IsKicking;
                    if (sawKick && hero.State.Phase == MeleePhase.Ready) break;
                }
                Assert.That(sawKick, Is.True, "The unarmed kick must actually enter its committed phase.");
                Assert.That(hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
                Vector3 beforeStep = hero.transform.position;
                Assert.That(hero.TryStep(Vector2.right), Is.True);
                for (int frame = 0; frame < 90 && hero.State.Phase != MeleePhase.Ready; frame++)
                { hero.Step(ImpactFrameSeconds); yield return null; }
                Assert.That(Vector3.Distance(beforeStep, hero.transform.position), Is.GreaterThan(.6f));
                Assert.That(hero.IsWeaponDropped, Is.True);
                Assert.That(hero.State.Health, Is.EqualTo(health));
                Assert.That(ReadImpactMember(ReadImpactMember(hero, "supportGrip"), "regripAllowed"), Is.EqualTo(false),
                    "Completing unarmed movement must not invite the left hand to reach for a missing bar.");

                // One ordinary blow can correctly be caught by the widened step
                // stance. Use the existing catastrophic-impact fixture pattern
                // for the distinct contract that an unarmed actor can fall again.
                for (int frame = 0; frame < 120 && (hero.Footwork.TransferringFoot ||
                    hero.ImpactMotion.MovementLandingActive || hero.Footwork.RecoveryEpisodeActive ||
                    ReadImpactMember(hero, "impactRecoveryGrace") is float grace && grace > 0f); frame++)
                { hero.Step(ImpactFrameSeconds); yield return null; }
                for (int tick = 0; tick < 120 && !hero.IsKnockedDown; tick++)
                {
                    ApplyControlledImpact(hero, hero.transform.forward, 500f);
                    hero.Step(CombatTestRoot.SimulationStep);
                    yield return null;
                }
                Assert.That(hero.IsKnockedDown && hero.IsRagdollActive, Is.True,
                    "A living unarmed actor must still hand catastrophic impacts to physics. " + DescribeImpactRecoveryGate(hero));
                yield return FinishIsolatedRecovery(hero, "A second fall must also finish without a weapon or regrip.");
                Assert.That(hero.IsWeaponDropped && hero.CombatFocused, Is.True);
                Assert.That(hero.State.Health, Is.EqualTo(health));
                yield return CaptureFocusGameView("recovery-unarmed-standing");

                // Resume the same opponent mind rather than resetting either actor.
                int decision = root.OpponentDecisionSequence;
                for (int frame = 0; frame < 900 && hero.State.Health >= health; frame++)
                { root.Tick(ImpactFrameSeconds); yield return null; }
                Assert.That(root.OpponentDecisionSequence, Is.GreaterThan(decision));
                Assert.That(hero.State.Health, Is.LessThan(health), "The living unarmed hero remains vulnerable to the hostile NPC.");
                Assert.That(root.OpponentRound, Is.EqualTo(round));
                Assert.That(root.RoundFinished, Is.False);
                float injuredHealth = hero.State.Health;
                hero.SetPresentationFrozen(false);
                yield return FinishIsolatedRecovery(hero, "A further unarmed injury must also release recovery ownership.");
                Assert.That(hero.State.Health, Is.EqualTo(injuredHealth));

                var pickup = hero.Weapon.GetComponent<CombatDroppedWeaponPickup>();
                Assert.That(pickup, Is.Not.Null);
                Vector3 stand = hero.Weapon.transform.position + Vector3.right * .85f;
                stand.y = hero.transform.position.y;
                root.Player.Motor.Teleport(stand);
                Physics.SyncTransforms();
                for (int frame = 0; frame < 8; frame++) yield return null;
                Assert.That(pickup.CanInteract(root.Player.Interactor), Is.True);
                Assert.That(root.Player.Interactor.ActiveInteractable, Is.SameAs(pickup));
                Assert.That(LocalizationService.Get(pickup.PromptKey), Does.Contain("E"), "The pickup prompt names its activation key.");
                int inventoryCount = GameSessionState.GetInventoryItemCount(InventoryItemId.CombatCrowbar);
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return null;
                WorldItemFoundScreen found = WorldItemFoundScreen.For(root.Player.Interactor);
                Assert.That(found.IsPresenting, Is.True);
                Assert.That(pickup.TryBeginPickup(root.Player.Interactor), Is.False, "A presenting item cannot open a duplicate screen.");
                yield return WaitFor(() => found.IsShowing, "The crowbar inspection did not reach its shared item panel.");
                yield return null;
                Assert.That(found.ActiveItemId, Is.EqualTo(InventoryItemId.CombatCrowbar));
                Assert.That(ReadImpactMember(found, "heldModel"), Is.SameAs(originalWeapon.transform),
                    "The shared screen inspects the actual dropped authored model.");
                Rigidbody weaponBody = hero.Weapon.GetComponent<Rigidbody>();
                Assert.That(weaponBody.interpolation, Is.EqualTo(RigidbodyInterpolation.None),
                    "Physics interpolation must yield the inspected model to the shared camera-local presenter.");
                Bounds previewBounds = WorldItemInspectionPresenter.CalculateWorldBounds(originalWeapon.transform);
                Vector3 previewCenter = root.CameraFollow.Camera.WorldToViewportPoint(previewBounds.center);
                Assert.That(previewCenter.x, Is.InRange(.2f, .8f), "The actual rendered crowbar must be centered in the item view.");
                Assert.That(previewCenter.y, Is.InRange(.2f, .8f), "The model must not remain at its former floor position.");
                Assert.That(previewCenter.z, Is.InRange(.2f, 1.5f), "The shared screen must hold the model near the viewing camera.");
                Assert.That(hero.IsWeaponDropped, Is.True);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.CombatCrowbar), Is.EqualTo(inventoryCount));
                yield return CaptureFocusGameView("recovery-crowbar-pickup");
                input.Press(keyboard.escapeKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.escapeKey, queueEventOnly: true);
                yield return WaitFor(() => !found.IsPresenting, "Cancelled weapon pickup kept its modal lock.");
                Assert.That(hero.IsWeaponDropped, Is.True);
                Assert.That(hero.Weapon.activeInHierarchy, Is.True);
                Assert.That(weaponBody.interpolation, Is.EqualTo(RigidbodyInterpolation.Interpolate),
                    "Cancelling inspection returns the released object to its original physical interpolation.");
                Assert.That(root.Player.Motor.InputEnabled, Is.True);
                yield return null;
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return WaitFor(() => found.IsShowing, "A cancelled pickup could not be deliberately opened again.");
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                Assert.That(found.Confirm(), Is.False, "A held confirmation cannot equip the same object twice.");
                yield return WaitFor(() => !found.IsPresenting, "Taking the weapon kept its modal lock.");
                for (int frame = 0; frame < 4; frame++) yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return null;
                Assert.That(found.IsPresenting, Is.False, "The close press must not reopen a world item.");
                Assert.That(hero.IsWeaponDropped, Is.False);
                Assert.That(hero.Weapon, Is.SameAs(originalWeapon));
                Assert.That(hero.Weapon.activeInHierarchy && hero.Weapon.transform.IsChildOf(hero.transform), Is.True);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.CombatCrowbar), Is.EqualTo(inventoryCount));
                Assert.That(hero.State.Health, Is.EqualTo(injuredHealth));
                Assert.That(root.OpponentRound, Is.EqualTo(round));
                Assert.That(hero.CombatFocused, Is.True);
                for (int frame = 0; frame < 12; frame++) { hero.Step(ImpactFrameSeconds); yield return null; }
                Assert.That(hero.TryAttack(), Is.True, "Reclaiming the original bar restores its combat commands.");

                // R restores the one original weapon and discards all escape state.
                root.ResetRound();
                yield return null;
                Assert.That(hero.Weapon, Is.SameAs(originalWeapon));
                Assert.That(hero.IsWeaponDropped || hero.IsKnockedDown || hero.IsRagdollActive, Is.False);
                Assert.That(hero.RecoveryBlockedSeconds, Is.Zero);
                Assert.That(hero.State.Health, Is.EqualTo(S.MaxHealth));
                Assert.That(hero.Weapon.GetComponents<Rigidbody>().Length, Is.EqualTo(1));
                Assert.That(root.GetComponentsInChildren<CombatDroppedWeaponPickup>(true).Length, Is.EqualTo(1));
                Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null)
                {
                    root.AutomaticSimulation = false;
                    WorldItemFoundScreen.For(root.Player.Interactor)?.Abandon();
                }
                if (cage != null) Object.Destroy(cage);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        private IEnumerator FinishIsolatedRecovery(CombatActor actor, string failure)
        {
            for (int frame = 0; frame < 720 && (actor.IsKnockedDown || actor.IsRagdollActive ||
                actor.State.Phase != MeleePhase.Ready || actor.ImpactMotion.IsActive ||
                actor.Footwork.RecoveryEpisodeActive); frame++)
            { actor.Step(ImpactFrameSeconds); yield return null; }
            Assert.That(actor.IsKnockedDown || actor.IsRagdollActive || actor.State.Phase != MeleePhase.Ready ||
                actor.ImpactMotion.IsActive || actor.Footwork.RecoveryEpisodeActive, Is.False,
                failure + " " + DescribeImpactRecoveryGate(actor));
        }

        private GameObject CreateRecoveryWeaponCage(CombatActor actor)
        {
            var result = new GameObject("Test trapped weapon cage");
            result.transform.SetParent(root.transform, false);
            result.transform.SetPositionAndRotation(actor.Weapon.transform.position, actor.Weapon.transform.rotation);
            Bounds bounds = default;
            bool first = true;
            foreach (CombatWeaponGeometry.Segment segment in CombatWeaponGeometry.Segments)
            {
                foreach (Vector3 point in new[] { segment.A, segment.B })
                {
                    var part = new Bounds(point, Vector3.one * segment.Radius * 2f);
                    if (first) { bounds = part; first = false; } else bounds.Encapsulate(part);
                }
            }
            const float thickness = .025f;
            // The physical core has 1 mm of contact with opposing panels. There
            // is no monotonic path through this fixed closed enclosure, and no
            // new panel follows the weapon when the arm makes its escape attempt.
            Vector3 innerSize = bounds.size - Vector3.one * .002f;
            for (int axis = 0; axis < 3; axis++)
                foreach (int side in new[] { -1, 1 })
                {
                    var panel = new GameObject("Test weapon cage panel");
                    panel.transform.SetParent(result.transform, false);
                    Vector3 center = bounds.center;
                    center[axis] += side * (innerSize[axis] + thickness) * .5f;
                    panel.transform.localPosition = center;
                    BoxCollider shape = panel.AddComponent<BoxCollider>();
                    Vector3 size = innerSize + Vector3.one * thickness * 2f;
                    size[axis] = thickness;
                    shape.size = size;
                }
            return result;
        }

        private void VerifyRecoveryFloorContactPaths(CombatActor actor)
        {
            Transform weapon = actor.Weapon.transform;
            Pose saved = new Pose(weapon.position, weapon.rotation);
            GameObject floor = null, wall = null;
            using var query = new CombatWeaponConstraint(actor);
            try
            {
                // A geometry-query fixture, while the real actor is recovering:
                // put the imported bar 1 mm above a known floor, so its 4 mm
                // presentation skin overlaps but the physical core stays clear.
                // Save/restore the prop only; no actor or recovery flags change.
                float floorY = actor.transform.position.y + .015f;
                Quaternion rotation = Quaternion.Euler(0f, 0f, 90f);
                float lowestCore = float.PositiveInfinity;
                foreach (CombatWeaponGeometry.Segment segment in CombatWeaponGeometry.Segments)
                    lowestCore = Mathf.Min(lowestCore, (rotation * segment.A).y - segment.Radius,
                        (rotation * segment.B).y - segment.Radius);
                Vector3 shoulder = FindAnatomicalBone(actor, "upper_arm.R").position;
                Vector3 origin = shoulder + new Vector3(1.1f, 0f, .4f);
                origin.y = floorY - lowestCore + .001f;
                Pose from = new Pose(origin, rotation);
                Pose up = new Pose(origin + Vector3.up * .008f, from.rotation);
                floor = new GameObject("Test recovery contact floor");
                floor.transform.SetParent(root.transform, false);
                floor.transform.position = new Vector3(origin.x - .25f, floorY - .02f, origin.z + .07f);
                BoxCollider floorShape = floor.AddComponent<BoxCollider>();
                floorShape.size = new Vector3(1.1f, .04f, .5f);
                weapon.SetPositionAndRotation(from.position, from.rotation);
                Physics.SyncTransforms();
                query.BeginRecoveryContact();
                var contacts = ReadImpactMember(query, "recoveryFloorContacts") as IList;
                Assert.That(contacts, Is.Not.Null);
                if (contacts.Count == 0)
                {
                    string diagnostic = $"recovering={actor.Ragdoll.IsRecovering} dropped={actor.IsWeaponDropped} " +
                        $"root={actor.transform.position} shoulder={shoulder} weapon={weapon.position} " +
                        $"floor={floorShape.bounds} lowestCore={lowestCore:F6} " +
                        $"obstacles={(ReadImpactMember(query, "obstacles") as IList)?.Count}";
                    foreach (CombatWeaponGeometry.Segment segment in CombatWeaponGeometry.Segments)
                    {
                        Vector3 a = from.position + from.rotation * segment.A;
                        Vector3 b = from.position + from.rotation * segment.B;
                        object[] arguments = { segment, a, b, floorShape, Vector3.zero };
                        float depth = (float)InvokeImpactProbe(query, "RecoveryFloorDepth", arguments);
                        diagnostic += $" [r={segment.Radius:F5} y={Mathf.Min(a.y, b.y) - segment.Radius:F5} " +
                            $"depth={depth:F6} normal={arguments[4]} core=" +
                            InvokeImpactProbe(query, "RecoveryCoreClear", segment, a, b, floorShape) + "]";
                    }
                    Assert.Fail("The fixture must capture a real skin-only floor contact. " + diagnostic);
                }
                bool FloorPath(Pose a, Pose b) => (bool)InvokeImpactProbe(query, "RecoveryFloorPathClear", a, b);
                bool Sweep(Pose a, Pose b) => (bool)InvokeImpactProbe(query, "SweepClear", a, b);
                Assert.That(FloorPath(from, up), Is.True, "Increasing floor clearance must be allowed during a rise.");
                Assert.That(FloorPath(from, new Pose(origin - Vector3.up * .002f, from.rotation)), Is.False,
                    "The same exception cannot deepen the original contact.");
                Assert.That(FloorPath(up, from), Is.False, "A released contact cannot be re-entered later in a path.");
                Assert.That(Sweep(from, up), Is.True, "Native sweeps must admit the checked floor escape too.");

                // A new wall is never an existing contact, even when the floor
                // release itself is valid and both path endpoints clear it.
                Pose side = new Pose(up.position + Vector3.forward * .5f, up.rotation);
                wall = new GameObject("Test new recovery path obstruction");
                wall.transform.SetParent(root.transform, false);
                wall.transform.position = origin + new Vector3(-.25f, .08f, .3f);
                wall.AddComponent<BoxCollider>().size = new Vector3(1.1f, .25f, .01f);
                Physics.SyncTransforms();
                InvokeImpactProbe(query, "GatherObstacles");
                Assert.That(Sweep(up, side), Is.False, "The complete escape path must still stop at a newly crossed wall.");
            }
            finally
            {
                weapon.SetPositionAndRotation(saved.position, saved.rotation);
                if (floor != null) { floor.SetActive(false); Object.Destroy(floor); }
                if (wall != null) { wall.SetActive(false); Object.Destroy(wall); }
                Physics.SyncTransforms();
            }
        }

        private void VerifyReleasedWeaponStandingClearance(CombatActor actor)
        {
            Transform weapon = actor.Weapon.transform;
            Rigidbody body = actor.Weapon.GetComponent<Rigidbody>();
            Pose saved = new Pose(weapon.position, weapon.rotation);
            GameObject obstruction = null;
            try
            {
                Vector3 center = actor.transform.position + Vector3.up * .8f;
                body.position = center;
                body.rotation = Quaternion.identity;
                weapon.SetPositionAndRotation(center, Quaternion.identity);
                Physics.SyncTransforms();
                object pose = ReadImpactMember(actor, "knockdownPose");
                Assert.That(InvokeImpactProbe(pose, "HasStandingClearance"), Is.EqualTo(true),
                    "The actor's own released crowbar must not imprison its standing body.");
                Assert.That(body.detectCollisions && !body.isKinematic, Is.True,
                    "Body clearance does not disable the released bar's physical contacts.");
                obstruction = new GameObject("Test foreign standing clearance obstruction");
                obstruction.transform.SetParent(root.transform, false);
                obstruction.transform.position = center;
                obstruction.AddComponent<BoxCollider>().size = Vector3.one * .12f;
                Physics.SyncTransforms();
                Assert.That(InvokeImpactProbe(pose, "HasStandingClearance"), Is.EqualTo(false),
                    "The exception for the owned bar cannot admit another object into standing clearance.");
            }
            finally
            {
                body.position = saved.position;
                body.rotation = saved.rotation;
                weapon.SetPositionAndRotation(saved.position, saved.rotation);
                if (obstruction != null) { obstruction.SetActive(false); Object.Destroy(obstruction); }
                Physics.SyncTransforms();
            }
        }
    }
}
