using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using static BarPromenade.Tests.PlayMode.CombatTuning;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_RecoveryStepsStartImmediatelyAndPreserveLiveSupport()
        {
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            var consume = typeof(CombatTestRoot).GetMethod("UpdateCombatInput",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var catchElapsed = typeof(CombatFootwork).GetField("settling",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var catchDuration = typeof(CombatFootwork).GetField("settleDuration",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
                yield return EnterRange();
                Assert.That(consume, Is.Not.Null);
                Assert.That(S.StepDistance, Is.EqualTo(1f));

                foreach (bool hero in new[] { true, false })
                foreach (bool late in new[] { false, true })
                {
                    PlacePair(.82f);
                    for (int frame = 0; frame < 6; frame++)
                    { root.Tick(1f / 60f); yield return null; }
                    CombatActor victim = hero ? root.Hero : root.Opponent;
                    CombatActor shover = hero ? root.Opponent : root.Hero;
                    Assert.That(shover.RequestAttack(), Is.True);
                    bool caught = false;
                    for (int tick = 0; tick < 180 && !caught; tick++)
                    {
                        root.Tick(CombatTestRoot.SimulationStep);
                        yield return null;
                        Assert.That(victim.IsKnockedDown, Is.False, victim.Footwork.SupportDiagnostics);
                        caught = victim.Footwork.CatchStepActive && (late
                            ? victim.Footwork.CatchStepCount == 2 && victim.Footwork.CatchStepProgress >= .9f &&
                              victim.Footwork.CatchStepProgress < .99f
                            : victim.Footwork.CatchStepCount == 1 && victim.Footwork.CatchStepProgress > 0f &&
                              victim.Footwork.CatchStepProgress < .5f);
                    }
                    Assert.That(caught, Is.True, $"A real shove must create the requested catch: hero={hero}, late={late}.");
                    if (late)
                    {
                        float remaining = .99f * (float)catchDuration.GetValue(victim.Footwork) -
                            (float)catchElapsed.GetValue(victim.Footwork);
                        victim.AdvanceSimulation(remaining);
                        victim.Present();
                        Assert.That(victim.Footwork.CatchStepProgress, Is.EqualTo(.99f).Within(.0001f));
                    }
                    else
                    {
                        Assert.That(victim.ImpactMotion.BalanceLoad, Is.GreaterThan(.65f),
                            $"The early escape must exercise a live load that used to block steps: hero={hero}, " +
                            $"load={victim.ImpactMotion.BalanceLoad:F3}, {victim.Footwork.SupportDiagnostics}");
                        // Keep a long rules reaction as well as the live physical
                        // catch, proving that the old .20s stun-tail gate is gone.
                        Assert.That(victim.State.ReceiveShove(.7f), Is.True);
                        Assert.That(victim.State.ActionRemaining, Is.GreaterThan(S.AttackBufferSeconds));
                        victim.Present();
                    }
                    Assert.That(victim.HasAttackBalance, Is.False);
                    Assert.That(victim.Footwork.JournalLeftSupport || victim.Footwork.JournalRightSupport, Is.True);
                    Assert.That(victim.ImpactMotion.WantsKnockdown, Is.False);
                    int sequence = victim.State.AttackSequence;
                    int recovery = victim.ImpactMotion.RecoverySequence;
                    int landings = victim.ImpactMotion.LandedRecoverySteps;
                    float stamina = victim.State.Stamina;
                    Vector3 velocity = victim.ImpactMotion.Velocity;
                    Vector3 rotation = victim.ImpactMotion.Rotation;
                    Vector3 start = victim.transform.position;
                    if (late) Assert.That(victim.State.RequestRecoveryCharge(), Is.True);
                    Vector2 direction = Vector2.down;
                    Vector3 worldDirection = -victim.transform.forward;
                    if (hero)
                    {
                        Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.True,
                            $"The fixture must own gameplay input before pressing Space: late={late}.");
                        Assert.That(Keyboard.current, Is.SameAs(keyboard));
                        Assert.That(keyboard.spaceKey.isPressed, Is.False,
                            $"The previous recovery press must have been released: late={late}.");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.S, Key.Space));
                        InputSystem.Update();
                        Assert.That(GameInput.WasPressed(GameInputAction.CombatStep, GameInputContext.Gameplay), Is.True,
                            $"One input update must deliver the new press: late={late}, frame={Time.frameCount}, " +
                            $"held={keyboard.spaceKey.isPressed}, raw={keyboard.spaceKey.ReadValue()}, " +
                            $"gameplay={GameInput.CanRead(GameInputContext.Gameplay)}, current={Keyboard.current == keyboard}.");
                        Assert.That((bool)consume.Invoke(root, null), Is.True);
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        InputSystem.Update();
                        Assert.That(keyboard.spaceKey.isPressed, Is.False);
                    }
                    else Assert.That(victim.TryStep(direction), Is.True);
                    Assert.That(victim.State.Phase, Is.EqualTo(MeleePhase.Step), "The admitted press starts without waiting for catch.");
                    Assert.That(victim.State.HasBufferedStep, Is.False);
                    Assert.That(victim.State.HasBufferedCharge, Is.False);
                    Assert.That(victim.State.AttackSequence, Is.EqualTo(sequence + 1));
                    Assert.That(victim.State.Stamina, Is.EqualTo(stamina - S.StepCost).Within(.001f));
                    Assert.That(victim.ActiveClipName, Is.EqualTo("CombatStepBackward"));
                    Assert.That(victim.Footwork.RecoveryStepOwnsFeet, Is.True);
                    Assert.That(victim.ImpactMotion.RecoverySequence, Is.EqualTo(recovery));
                    Assert.That(victim.ImpactMotion.LandedRecoverySteps, Is.EqualTo(landings), "Handoff cannot invent a catch landing.");
                    Assert.That(victim.ImpactMotion.Velocity, Is.EqualTo(velocity), "A step cannot erase the incoming impulse.");
                    Assert.That(victim.ImpactMotion.Rotation, Is.EqualTo(rotation));
                    Assert.That(victim.transform.position, Is.EqualTo(start), "Starting an escape is not a teleport.");
                    Assert.That(victim.TryStep(Vector2.right), Is.False, "A second press cannot restart committed travel.");
                    Assert.That(victim.State.Stamina, Is.EqualTo(stamina - S.StepCost).Within(.001f));
                    if (hero && late)
                    {
                        Assert.That(root.PauseMenu.Open(), Is.True);
                        for (int frame = 0; frame < 4; frame++)
                        { root.Tick(S.AttackBufferSeconds); yield return null; }
                        Assert.That(victim.State.StepElapsed, Is.Zero);
                        Assert.That(victim.transform.position, Is.EqualTo(start));
                        Assert.That(victim.ImpactMotion.Velocity, Is.EqualTo(velocity));
                        Assert.That(root.PauseMenu.Cancel(), Is.True);
                        yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause must release the recovery step.");
                    }
                    string subject = (hero ? "supported-step-hero" : "supported-step-opponent") + (late ? "-late" : "-early");
                    CaptureInertiaFrame(victim, subject, 0);
                    bool middleCaptured = false;
                    int landingsBeforeReturn = landings;
                    Vector3 stepEnd = start;
                    for (int tick = 0; tick < Mathf.CeilToInt((S.StepDurationSeconds + .35f) /
                        CombatTestRoot.SimulationStep); tick++)
                    {
                        root.Tick(CombatTestRoot.SimulationStep);
                        yield return null;
                        Assert.That(victim.IsKnockedDown, Is.False,
                            $"A reachable recovery step keeps support through its full travel and return: hero={hero}, late={late}, " +
                            $"tick={tick}, clip={victim.ActiveClipName}, travel={victim.State.StepTravelProgress:F3}, " +
                            $"load={victim.ImpactMotion.BalanceLoad:F3}, {victim.Footwork.SupportDiagnostics}");
                        if (victim.State.Phase == MeleePhase.Step)
                        {
                            stepEnd = victim.transform.position;
                            landingsBeforeReturn = victim.ImpactMotion.LandedRecoverySteps;
                            Assert.That(victim.Footwork.JournalLeftSupport || victim.Footwork.JournalRightSupport, Is.True,
                                $"Ground support must be measured: hero={hero}, late={late}, tick={tick}, " +
                                $"clip={victim.ActiveClipName}, elapsed={victim.State.StepElapsed:F3}, " +
                                $"travel={victim.State.StepTravelProgress:F3}, load={victim.ImpactMotion.BalanceLoad:F3}, " +
                                victim.Footwork.SupportDiagnostics);
                            if (!middleCaptured && victim.State.StepTravelProgress >= .5f)
                            { CaptureInertiaFrame(victim, subject, 1); middleCaptured = true; }
                        }
                    }
                    Assert.That(middleCaptured, Is.True);
                    Assert.That(victim.State.Phase, Is.EqualTo(MeleePhase.Ready));
                    Assert.That(victim.State.AttackSequence, Is.EqualTo(sequence + 1));
                    Assert.That(Vector3.Dot(stepEnd - start, worldDirection), Is.GreaterThan(.95f),
                        "The metre of owned step travel remains useful while the existing impulse also moves the body.");
                    Assert.That(landingsBeforeReturn, Is.EqualTo(landings + 2), "The directed shuffle lands each foot exactly once.");
                    Assert.That(victim.ImpactMotion.LandedRecoverySteps, Is.GreaterThanOrEqualTo(landingsBeforeReturn));
                }

                // Rules reactions may outlast the physical load. All three
                // reactions still allow the same measured emergency handoff.
                foreach (MeleePhase phase in new[] { MeleePhase.Stagger, MeleePhase.GuardImpact, MeleePhase.GuardBroken })
                {
                    PlacePair(4f);
                    CombatActor actor = root.Hero;
                    for (int frame = 0; frame < 3; frame++)
                    { root.Tick(1f / 60f); yield return null; }
                    if (phase == MeleePhase.Stagger) actor.State.ReceiveShove(.7f);
                    else
                    {
                        actor.State.SetBlocking(true);
                        actor.State.Advance(S.ParryWindowSeconds + .01f);
                        actor.State.ReceiveHit(1f, phase == MeleePhase.GuardBroken ? 101f : 1f, true);
                    }
                    Assert.That(actor.State.Phase, Is.EqualTo(phase));
                    actor.Present();
                    Assert.That(actor.HasAttackBalance, Is.True);
                    Vector3 start = actor.transform.position;
                    float stamina = actor.State.Stamina;
                    Assert.That(actor.TryStep(Vector2.down), Is.True, "A settled physical response cannot leave a stale stun-tail gate.");
                    Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Step));
                    Assert.That(actor.State.Stamina, Is.EqualTo(stamina - S.StepCost).Within(.001f));
                    for (int tick = 0; tick < Mathf.CeilToInt((S.StepDurationSeconds + .1f) / CombatTestRoot.SimulationStep); tick++)
                    { root.Tick(CombatTestRoot.SimulationStep); yield return null; }
                    Assert.That(actor.IsKnockedDown, Is.False);
                    Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Ready));
                    Assert.That(Vector3.Dot(actor.transform.position - start, Vector3.back), Is.EqualTo(1f).Within(.045f));
                }

                foreach (bool hero in new[] { true, false })
                {
                    PlacePair(4f);
                    CombatActor actor = hero ? root.Hero : root.Opponent;
                    for (int frame = 0; frame < 3; frame++)
                    { root.Tick(1f / 60f); yield return null; }
                    for (int tick = 0; tick < 120 && !actor.IsKnockedDown; tick++)
                    {
                        ApplyControlledImpact(actor, actor.transform.forward, 500f);
                        root.Tick(CombatTestRoot.SimulationStep);
                        yield return null;
                    }
                    Assert.That(actor.IsKnockedDown, Is.True, "A catastrophic physical impact must retain its real fall.");
                    float stamina = actor.State.Stamina;
                    Assert.That(actor.TryStep(Vector2.down), Is.False);
                    Assert.That(actor.State.Stamina, Is.EqualTo(stamina));
                    Assert.That(actor.State.HasBufferedStep, Is.False);
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator Range_PostCatchStepAndRetreatKeepFinitePhysicalLandings()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            yield return EnterRange();
            GameObject obstacle = null;
            try
            {
                foreach (bool hero in new[] { true, false })
                foreach (int movement in new[] { 0, 1, 2 })
                {
                    PlacePair(.82f);
                    for (int frame = 0; frame < 6; frame++)
                    { root.Tick(1f / 60f); yield return null; }
                    CombatActor actor = hero ? root.Hero : root.Opponent;
                    CombatActor shover = hero ? root.Opponent : root.Hero;
                    Assert.That(shover.RequestAttack(), Is.True);
                    bool released = false;
                    for (int tick = 0; tick < 240 && !released; tick++)
                    {
                        root.Tick(CombatTestRoot.SimulationStep); yield return null;
                        Assert.That(actor.IsKnockedDown, Is.False, actor.Footwork.SupportDiagnostics);
                        released = actor.Footwork.CatchStepCount == 2 && !actor.Footwork.CatchStepActive &&
                            actor.HasAttackBalance && actor.State.Phase == MeleePhase.Ready;
                    }
                    Assert.That(released, Is.True, $"Two real catches must release ordinary movement: hero={hero}.");
                    Assert.That(actor.ImpactMotion.IsActive, Is.True,
                        "This regression exercises the old response after action admission has reopened.");
                    int impacts = actor.ReceivedImpactCount, recovery = actor.ImpactMotion.RecoverySequence;
                    int catches = actor.Footwork.CatchStepCount, landings = actor.ImpactMotion.LandedRecoverySteps;
                    Vector3 start = actor.transform.position;
                    Vector3 direction = movement == 0 ? actor.transform.right : -actor.transform.forward;
                    float stamina = actor.State.Stamina;
                    if (movement == 0)
                    {
                        Assert.That(actor.TryStep(Vector2.right), Is.True);
                        Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Step));
                        Assert.That(actor.Footwork.RecoveryStepOwnsFeet, Is.False,
                            "An ordinary step must not become a recovery action merely because the old impact is active.");
                        Assert.That(actor.State.Stamina, Is.EqualTo(stamina - S.StepCost).Within(.001f));
                    }
                    bool sawForecast = false, captured = false, obstructionChecked = false;
                    int ticks = Mathf.CeilToInt((movement == 0 ? S.StepDurationSeconds + .4f : .9f) /
                        CombatTestRoot.SimulationStep);
                    for (int tick = 0; tick < ticks; tick++)
                    {
                        if (movement == 0) root.Tick(CombatTestRoot.SimulationStep);
                        else AdvanceAchievedCombatTravel(actor, tick < 60 ? direction * 1.6f : Vector3.zero,
                            CombatTestRoot.SimulationStep);
                        yield return null;
                        string context = $"hero={hero}, movement={movement}, tick={tick}, phase={actor.State.Phase}, " +
                            $"load={actor.ImpactMotion.BalanceLoad:F3}, {actor.Footwork.SupportDiagnostics}";
                        Assert.That(actor.ReceivedImpactCount, Is.EqualTo(impacts), context);
                        Assert.That(actor.ImpactMotion.RecoverySequence, Is.EqualTo(recovery), context);
                        Assert.That(actor.Footwork.CatchStepCount, Is.EqualTo(catches),
                            "Ordinary plants cannot replenish or consume the exhausted automatic catch budget. " + context);
                        Assert.That(actor.ImpactMotion.LandedRecoverySteps, Is.EqualTo(landings), context);
                        sawForecast |= actor.ImpactMotion.MovementLandingActive;
                        if (movement == 2 && obstacle == null && actor.ImpactMotion.MovementLandingActive)
                        {
                            obstacle = new GameObject("Test blocked walking landing");
                            obstacle.transform.position = actor.ImpactMotion.MovementLandingTarget + Vector3.up * .15f;
                            obstacle.AddComponent<BoxCollider>().size = new Vector3(.18f, .3f, .18f);
                            Physics.SyncTransforms();
                        }
                        else if (obstacle != null)
                        {
                            Assert.That(actor.ImpactMotion.MovementLandingActive, Is.False,
                                "A newly obstructed endpoint must withdraw its finite balance forecast. " + context);
                            obstructionChecked = true;
                            break;
                        }
                        Assert.That(actor.IsKnockedDown, Is.False,
                            "Reachable ordinary travel must keep its real support after catch. " + context);
                        if (!captured && (movement == 0 ? actor.State.StepTravelProgress >= .5f : tick >= 12))
                        {
                            CaptureInertiaFrame(actor, (hero ? "post-catch-hero" : "post-catch-opponent") +
                                (movement == 0 ? "-step" : "-retreat"), 0);
                            captured = true;
                        }
                    }
                    Assert.That(sawForecast, Is.True, $"A real finite landing must be offered: hero={hero}, movement={movement}.");
                    if (movement == 2)
                    {
                        Assert.That(obstructionChecked, Is.True);
                        Object.Destroy(obstacle); obstacle = null;
                        yield return null;
                    }
                    else
                    {
                        Assert.That(captured, Is.True);
                        Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Ready));
                        Assert.That(Vector3.Dot(actor.transform.position - start, direction), Is.GreaterThan(.95f * (movement == 0 ? 1f : .8f)));
                        Assert.That(actor.ImpactMotion.MovementLandingActive, Is.False,
                            "A completed step or stopped gait cannot retain a future landing indefinitely.");
                    }
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (obstacle != null) Object.Destroy(obstacle);
                if (root != null) root.AutomaticSimulation = false;
            }
        }

        [UnityTest]
        public IEnumerator Range_TacticalRecoveryStepsAndFairOpponent()
        {
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            GameObject wall = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
                yield return EnterRange();
                var recoveries = new float[3];
                var outcomes = new[] { MeleeAttackOutcome.Hit, MeleeAttackOutcome.Obstacle, MeleeAttackOutcome.Miss };
                for (int i = 0; i < outcomes.Length; i++)
                {
                    PlacePair(i == 2 ? 3f : 1.1f);
                    root.Opponent.SetBlock(i == 1);
                    Assert.That(root.Hero.TryAttack(), Is.True);
                    int contactTicks = 0;
                    if (i == 1)
                    {
                        while (root.Hero.State.AttackOutcome != MeleeAttackOutcome.Obstacle &&
                            contactTicks < ContactTicks(1f / CombatTestRoot.SimulationStep) + 4)
                        { root.Tick(CombatTestRoot.SimulationStep); contactTicks++; }
                        Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.GuardImpact));
                        Assert.That(root.Opponent.TryAttack(), Is.False, "A normal block does not grant an immediate counter.");
                        Assert.That(root.Hero.WeaponClashCount, Is.EqualTo(1), "Recovery follows a real metal interception.");
                        Assert.That(root.Hero.State.RecoveryRemaining, Is.EqualTo(S.ObstacleRecoverySeconds).Within(.0001f),
                            "The first physical block starts one complete wall recoil immediately.");
                    }
                    root.Tick(Mathf.Max(0f, IntoRecoverySeconds - contactTicks * CombatTestRoot.SimulationStep));
                    Assert.That(root.Hero.State.AttackOutcome, Is.EqualTo(outcomes[i]), "Actual weapon contact must set recovery.");
                    recoveries[i] = root.Hero.State.RecoveryRemaining;
                    Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Recovery));
                    bool queued = root.Hero.TryStep(Vector2.down);
                    Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Recovery),
                        "A committed attack cannot escape into a step; at most the press waits for the boundary.");
                    Assert.That(queued, Is.EqualTo(root.Hero.State.HasBufferedStep));
                    if (i == 1)
                    {
                        Assert.That(root.Opponent.State.Health, Is.EqualTo(S.MaxHealth));
                    }
                    if (i == 0)
                    {
                        Assert.That(root.Opponent.State.Health, Is.LessThan(S.MaxHealth));
                        Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(1));
                    }
                }
                // A wall recoil starts at contact, before the ordinary active arc
                // ends, so remaining times at a shared later clock are not ordered.
                Assert.That(recoveries[0], Is.LessThanOrEqualTo(S.HitRecoverySeconds));
                Assert.That(recoveries[1], Is.LessThanOrEqualTo(S.ObstacleRecoverySeconds));
                Assert.That(recoveries[2], Is.LessThanOrEqualTo(S.RecoverySeconds));

                var directions = new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
                var keys = new[] { keyboard.wKey, keyboard.sKey, keyboard.aKey, keyboard.dKey };
                var clips = new[] { "CombatStepForward", "CombatStepBackward", "CombatStepLeft", "CombatStepRight" };
                for (int i = 0; i < directions.Length; i++)
                {
                    PlacePair(4f);
                    root.AutomaticSimulation = true;
                    Vector3 start = root.Hero.transform.position;
                    Vector3 direction = new Vector3(directions[i].x, 0f, directions[i].y);
                    var legs = new WalkingLegProbe(root.Hero);
                    input.Press(keys[i], queueEventOnly: true);
                    input.Press(keyboard.spaceKey, queueEventOnly: true);
                    yield return null;
                    Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Step));
                    Assert.That(root.Hero.ActiveClipName, Is.EqualTo(clips[i]));
                    Assert.That(root.Hero.State.Stamina, Is.EqualTo(AfterStep).Within(.001f));
                    Assert.That(root.Hero.Body.enabled, Is.True, "A step keeps its ordinary movement capsule.");
                    Assert.That(root.Player.Motor.ApplyOwnedDisplacement(new object(), Vector3.forward), Is.EqualTo(Vector3.zero));
                    Assert.That(root.Hero.TryAttack(), Is.False);
                    for (int frame = 0; frame < StepFrames + 17 && root.Hero.State.Phase == MeleePhase.Step; frame++)
                    { yield return null; legs.Sample(); AssertOpponentFramed(root.CameraFollow.Camera); }
                    input.Release(keys[i], queueEventOnly: true);
                    Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
                    Assert.That(Vector3.Dot(root.Hero.transform.position - start, direction), Is.EqualTo(S.StepDistance).Within(.045f));
                    legs.AssertMoving(2f, "A short step must move both real legs.");
                    for (int frame = 0; frame < 12; frame++) yield return null;
                    Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready), "Holding Space cannot repeat steps.");
                    Assert.That(root.Hero.State.Stamina, Is.GreaterThanOrEqualTo(AfterStep - .001f),
                        "Holding Space cannot spend again; the longer step may have reached the regeneration delay.");
                    input.Release(keyboard.spaceKey, queueEventOnly: true);
                    yield return null;
                    root.AutomaticSimulation = false;
                }

                PlacePair(4f);
                root.AutomaticSimulation = true;
                input.Press(keyboard.spaceKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.spaceKey, queueEventOnly: true);
                Assert.That(root.Hero.ActiveClipName, Is.EqualTo("CombatStepBackward"), "Space alone retreats.");
                Assert.That(root.PauseMenu.Open(), Is.True);
                Vector3 paused = root.Hero.transform.position;
                float elapsed = root.Hero.State.StepElapsed;
                for (int frame = 0; frame < 12; frame++) yield return null;
                Assert.That(root.Hero.transform.position, Is.EqualTo(paused));
                Assert.That(root.Hero.State.StepElapsed, Is.EqualTo(elapsed));
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release the step.");
                input.Press(keyboard.rKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.rKey, queueEventOnly: true);
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
                Assert.That(root.Hero.State.StepElapsed, Is.Zero);
                Assert.That(root.Hero.State.Stamina, Is.EqualTo(S.MaxStamina));
                yield return null;
                root.AutomaticSimulation = false;

                PlacePair(4f);
                wall = new GameObject("Short-step obstruction");
                wall.transform.SetParent(root.transform, false);
                wall.transform.position = new Vector3(.45f, 1f, 0f);
                wall.AddComponent<BoxCollider>().size = new Vector3(.08f, 2f, 3f);
                Physics.SyncTransforms();
                Assert.That(root.Hero.TryStep(Vector2.right), Is.True);
                root.Tick(.2f);
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Step), "A wall cannot refund the step's commitment.");
                Assert.That(root.Hero.ActiveClipName, Is.EqualTo("CombatReady"), "Blocked feet settle at the actual position.");
                Assert.That(root.Hero.State.Stamina, Is.EqualTo(AfterStep));
                Assert.That(root.Hero.TryAttack(), Is.False);
                root.Tick(.4f);
                Assert.That(root.Hero.transform.position.x, Is.LessThan(.15f), "The step cannot tunnel through a thin wall.");
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
                Vector3 stopped = root.Hero.transform.position;
                Object.Destroy(wall); wall = null;
                yield return null;
                root.Tick(.3f);
                Assert.That(Vector3.Distance(stopped, root.Hero.transform.position), Is.LessThan(.02f),
                    "Blocked travel is discarded, not stored until the wall disappears.");

                PlacePair(1.1f);
                Assert.That(root.Opponent.TryAttack(), Is.True);
                root.Tick(.44f);
                Assert.That(root.Hero.TryStep(Vector2.up), Is.True);
                root.Tick(.13f);
                Assert.That(root.Hero.State.Health, Is.LessThan(S.MaxHealth), "A mistimed step still receives actual weapon contact.");
                Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1));
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Stagger));
                Assert.That(root.Hero.State.StepElapsed, Is.Zero, "A hit cancels remaining step travel.");

                root.SetSparring(true);
                root.Hero.ResetActor(Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.forward);
                root.Opponent.ResetActor(new Vector3(0f, PlayerFactory.GroundedRootOffset, 1.25f), Vector3.back);
                Physics.SyncTransforms();
                input.Press(keyboard.sKey, queueEventOnly: true);
                // Start perception only after the real motor has achieved a
                // retreat. The pair stays close enough for pursuit to catch it
                // before the hero reaches an arena boundary and stops moving.
                yield return WaitFor(() => root.Player.Motor.PlanarVelocity.z < -.5f,
                    "The live retreat input did not produce actual backward movement.");
                root.AutomaticSimulation = true;
                yield return WaitFor(() => root.Opponent.State.Phase == MeleePhase.Windup,
                    "The opponent did not choose an attack against a retreating hero.");
                Assert.That(root.OpponentObservedVelocity.z, Is.LessThan(-.5f));
                Assert.That(root.OpponentDecisionSequence, Is.GreaterThan(1));
                Assert.That(Vector3.Distance(root.Hero.transform.position, root.Opponent.transform.position), Is.LessThan(1.1f),
                    "Observed retreat must make the opponent close in before committing.");
                Quaternion facing = root.Opponent.transform.rotation;
                input.Release(keyboard.sKey, queueEventOnly: true);
                root.AutomaticSimulation = false;
                yield return null; // Consume the input release without spending the held NPC windup.
                Vector3 sidestepStart = root.Hero.transform.position;
                Vector3 sidestepDirection = root.Hero.transform.right;
                Assert.That(root.Hero.TryStep(Vector2.right), Is.True,
                    "The post-pursuit target must start an actual lateral step before checking strike tracking.");
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Step));
                Assert.That(root.Hero.ActiveClipName, Is.EqualTo("CombatStepRight"));
                for (int tick = 0; tick < 30; tick++)
                {
                    Quaternion previousFacing = root.Opponent.transform.rotation;
                    root.Tick(CombatTestRoot.SimulationStep);
                    Assert.That(Quaternion.Angle(previousFacing, root.Opponent.transform.rotation),
                        Is.LessThanOrEqualTo(CombatActor.MaximumFacingSpeed * CombatTestRoot.SimulationStep + .05f),
                        "A sidestep is followed with the duel's bounded turn rate, without snapping the body.");
                }
                Assert.That(Vector3.Dot(root.Hero.transform.position - sidestepStart, sidestepDirection),
                    Is.GreaterThan(.2f), "The constrained controller must achieve the lateral step being tracked.");
                Assert.That(Quaternion.Angle(facing, root.Opponent.transform.rotation), Is.GreaterThan(1f),
                    $"The weapon preparation follows the opponent's changed position: npc={root.Opponent.State.Phase}, " +
                    $"hero={root.Hero.State.Phase}, bearing={TrackingBearing(root.Opponent, root.Hero):F3}, " +
                    $"balance={root.Opponent.ImpactMotion.BalanceLoad:F3}, supportRecovery={root.Opponent.Footwork.RecoveryEpisodeActive}, " +
                    $"npcElapsed={root.Opponent.State.AttackElapsed:F3}, hitStop={root.HitStopSecondsConsumed:F3}.");

                // A target at ninety degrees remains out of reach. Preparation
                // can correct its direction, but cannot snap around or extend the weapon.
                root.SetSparring(true);
                Vector3 ground = Vector3.up * PlayerFactory.GroundedRootOffset;
                root.Opponent.ResetActor(ground, Vector3.forward);
                root.Hero.ResetActor(ground + Vector3.right * 2f, Vector3.left);
                Physics.SyncTransforms();
                Assert.That(root.Opponent.TryAttack(), Is.True);
                Quaternion committed = root.Opponent.transform.rotation;
                for (int tick = 0; tick < ContactTicks(120f); tick++)
                {
                    Quaternion previousFacing = root.Opponent.transform.rotation;
                    root.Tick(CombatTestRoot.SimulationStep);
                    Assert.That(Quaternion.Angle(previousFacing, root.Opponent.transform.rotation),
                        Is.LessThanOrEqualTo(CombatActor.MaximumFacingSpeed * CombatTestRoot.SimulationStep + .05f),
                        "Preparation and the live arc may only spend their simulation turn budget.");
                }
                Assert.That(Quaternion.Angle(committed, root.Opponent.transform.rotation), Is.GreaterThan(5f),
                    "The strike corrects toward a flanking opponent instead of keeping an obsolete line.");
                root.Tick(S.RecoverySeconds + CombatTestRoot.SimulationStep);
                Assert.That(root.Opponent.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Miss));
                Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Ready));
                Vector3 toHero = root.Hero.transform.position - root.Opponent.transform.position;
                toHero.y = 0f;
                Assert.That(Quaternion.Angle(committed, root.Opponent.transform.rotation), Is.GreaterThan(30f),
                    "A spent swing turns the body back toward the target during its recovery.");
                Assert.That(Vector3.Dot(root.Opponent.transform.forward, toHero.normalized), Is.GreaterThan(.35f),
                    "After the whiff the hero is back inside the guard cone.");

                root.SetSparring(true);
                root.Opponent.ResetActor(ground, Vector3.forward);
                root.Hero.ResetActor(ground + Vector3.right * 1.5f, Vector3.left);
                Physics.SyncTransforms();
                float BearingToHero() => Mathf.Abs(Vector3.SignedAngle(root.Opponent.transform.forward,
                    root.Hero.transform.position - root.Opponent.transform.position, Vector3.up));
                float flanked = BearingToHero();
                Assert.That(root.Opponent.State.ReceiveHit(S.Damage, S.BlockCost, false), Is.EqualTo(MeleeHitResult.Hit));
                Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Stagger));
                root.Tick(S.StaggerSeconds + CombatTestRoot.SimulationStep);
                Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Ready));
                Assert.That(flanked - BearingToHero(), Is.GreaterThan(15f),
                    "A rocked body turns toward the blow's source during the stagger.");
                Assert.That(root.ReturnToMenu(), Is.True);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                    !SceneTransitionService.IsTransitioning, "A step must not retain the scene or its input ownership.");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                if (wall != null) Object.Destroy(wall);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        /// <summary>Retreat and a late side step evade through actual reach and geometry;
        /// the swing that follows finds the whiffer still exposed.</summary>
        [UnityTest]
        public IEnumerator Range_StepsAtTheTellEvadeTheAuthoredArcAndTheStepAttackCountersTheWhiff()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            yield return EnterRange();
            try
            {
                // Use the shared 120 Hz clock for the real posed anatomy. Render
                // frames cannot supply a reliable step/counter timing boundary.
                ConfigureDuelMovementCapsule(root.Hero);
                ConfigureDuelMovementCapsule(root.Opponent);
                root.AutomaticSimulation = false;
                float counterStagger = S.StaggerSeconds + S.CounterHitStaggerBonus;
                var contactLeads = new float[2];
                for (int side = 0; side < contactLeads.Length; side++)
                {
                    PlacePair(CombatActor.WeaponSpacing);
                    yield return null;
                    yield return null;
                    PresentInertiaPose();
                    root.Hero.State.ObserveLateralCue(side == 0 ? -1 : 1);
                    Assert.That(root.Hero.TryObservedCounterAttack(), Is.True);
                    Assert.That(root.Hero.State.Swing, Is.EqualTo(side == 0 ? MeleeSwing.Forehand : MeleeSwing.Backhand));
                    for (int tick = 1; tick <= ContactTicks(120f) && contactLeads[side] == 0f; tick++)
                    {
                        root.Tick(CombatTestRoot.SimulationStep);
                        if (root.Opponent.ReceivedImpactCount > 0)
                            contactLeads[side] = root.Hero.State.AttackElapsed;
                    }
                    Assert.That(contactLeads[side], Is.GreaterThan(0f),
                        "Each imported short-counter clip must make real contact at the counter's closing distance.");
                }

                // A back step opens the gap past the crowbar's reach.
                PlacePair(1.1f);
                yield return null;
                yield return null;
                Vector3 heroStart = root.Hero.transform.position;
                Assert.That(root.Opponent.TryAttack(), Is.True);
                AdvanceDuel(.22f, CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.TryStep(Vector2.down), Is.True);
                for (int tick = 0; tick < 120 && (root.Hero.State.Phase == MeleePhase.Step ||
                    root.Opponent.State.Phase == MeleePhase.Windup || root.Opponent.State.Phase == MeleePhase.Active); tick++)
                    root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
                Assert.That(Vector3.Distance(heroStart, root.Hero.transform.position), Is.EqualTo(S.StepDistance).Within(.05f));
                Assert.That(root.Hero.State.Health, Is.EqualTo(S.MaxHealth), "A back step at the tell leaves the arc.");
                Assert.That(root.Opponent.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Miss));

                // The preparation follows an early side step. A later step can
                // leave the physical arc after its steering starts to taper.
                // Budget the imported contact itself, not only its .22 s windup.
                string evadedSide = null;
                for (int sideIndex = 0; sideIndex < contactLeads.Length; sideIndex++)
                {
                    PlacePair(1.1f);
                    yield return null;
                    yield return null;
                    Assert.That(root.Opponent.TryAttack(), Is.True);
                    float counterBudget = S.WindupSeconds + S.ActiveSeconds + S.RecoverySeconds -
                        S.StepDurationSeconds - contactLeads[sideIndex] - 2f * CombatTestRoot.SimulationStep;
                    float reaction = Mathf.Min(S.WindupSeconds - .12f, counterBudget);
                    Assert.That(reaction, Is.GreaterThan(.20f), "The measured counter still leaves a human reaction window.");
                    AdvanceDuel(reaction, CombatTestRoot.SimulationStep);
                    Vector2 side = sideIndex == 0 ? Vector2.left : Vector2.right;
                    Assert.That(root.Hero.TryStep(side), Is.True);
                    for (int tick = 0; tick < 120 && (root.Hero.State.Phase == MeleePhase.Step ||
                        root.Opponent.State.Phase == MeleePhase.Windup || root.Opponent.State.Phase == MeleePhase.Active); tick++)
                        root.Tick(CombatTestRoot.SimulationStep);
                    Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Recovery));
                    if (root.Hero.State.Health < S.MaxHealth) continue;
                    evadedSide = side == Vector2.left ? "left" : "right";
                    Assert.That(root.Opponent.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Miss));
                    Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready),
                        "The evading step must complete its ordinary travel and settle before the counter.");
                    Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Recovery),
                        "The step must finish while the missed swing is still exposed.");
                    // Close the lateral gap and square up the way a player would, then swing
                    // while the whiffer is still recovering.
                    Vector3 toOpponent = root.Opponent.transform.position - root.Hero.transform.position;
                    toOpponent.y = 0f;
                    root.Hero.Body.Move(toOpponent.normalized * Mathf.Max(0f, toOpponent.magnitude - CombatActor.WeaponSpacing));
                    root.Hero.transform.rotation = Quaternion.LookRotation(toOpponent.normalized);
                    Physics.SyncTransforms();
                    Assert.That(root.Hero.TryAttack(), Is.True);
                    Assert.That(root.Hero.State.IsChained, Is.True, "The real step's grace selects the short counter clip.");
                    Assert.That(root.Hero.State.Swing, Is.EqualTo(sideIndex == 0 ? MeleeSwing.Forehand : MeleeSwing.Backhand));
                    Assert.That(root.Opponent.State.RecoveryRemaining, Is.GreaterThan(contactLeads[sideIndex]),
                        "The imported blade must have time to reach the whiffer before its recovery closes.");
                    float before = root.Opponent.State.Health;
                    for (int tick = 0; tick < ContactTicks(120f) && root.Opponent.State.Health == before; tick++)
                        root.Tick(CombatTestRoot.SimulationStep);
                    Assert.That(root.Opponent.State.Health, Is.LessThan(before), "The swing after the evasion must reach the whiffer.");
                    Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Stagger));
                    Assert.That(root.Opponent.State.ActionRemaining, Is.EqualTo(counterStagger).Within(.04f),
                        "Punishing a whiff is a counter-hit.");
                    break;
                }
                Assert.That(evadedSide, Is.Not.Null, "At least one late side step must leave the physical arc.");
                Debug.Log("Combat step evasion side: " + evadedSide);
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
            }
        }
    }
}
