using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using static BarPromenade.Tests.PlayMode.CombatTuning;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
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
                var outcomes = new[] { MeleeAttackOutcome.Hit, MeleeAttackOutcome.Blocked, MeleeAttackOutcome.Miss };
                for (int i = 0; i < outcomes.Length; i++)
                {
                    PlacePair(i == 2 ? 3f : 1.1f);
                    root.Opponent.SetBlock(i == 1);
                    Assert.That(root.Hero.TryAttack(), Is.True);
                    int contactTicks = 0;
                    if (i == 1)
                    {
                        while (root.Hero.State.AttackOutcome != MeleeAttackOutcome.Blocked &&
                            contactTicks < ContactTicks(1f / CombatTestRoot.SimulationStep) + 4)
                        { root.Tick(CombatTestRoot.SimulationStep); contactTicks++; }
                        Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.GuardImpact));
                        Assert.That(root.Opponent.TryAttack(), Is.False, "A normal block does not grant an immediate counter.");
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
                    if (i == 0) Assert.That(root.Opponent.State.Health, Is.EqualTo(S.MaxHealth - S.Damage));
                }
                Assert.That(recoveries[1] - recoveries[0], Is.GreaterThan(S.BlockRecoverySeconds - S.HitRecoverySeconds - .03f));
                Assert.That(recoveries[2] - recoveries[1], Is.GreaterThan(S.RecoverySeconds - S.BlockRecoverySeconds - .05f));

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
                    Assert.That(root.Hero.Body.enabled, Is.True, "A step keeps the ordinary hittable capsule.");
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
                    Assert.That(root.Hero.State.Stamina, Is.EqualTo(AfterStep).Within(.001f));
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
                Assert.That(root.Hero.State.Health, Is.EqualTo(S.MaxHealth - S.Damage), "A mistimed step still receives actual weapon contact.");
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Stagger));
                Assert.That(root.Hero.State.StepElapsed, Is.Zero, "A hit cancels remaining step travel.");

                root.SetSparring(true);
                root.Hero.ResetActor(Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.forward);
                root.Opponent.ResetActor(new Vector3(0f, PlayerFactory.GroundedRootOffset, 1.6f), Vector3.back);
                Physics.SyncTransforms();
                root.AutomaticSimulation = true;
                input.Press(keyboard.sKey, queueEventOnly: true);
                yield return WaitFor(() => root.Opponent.State.Phase == MeleePhase.Windup,
                    "The opponent did not choose an attack against a retreating hero.");
                Assert.That(root.OpponentObservedVelocity.z, Is.LessThan(-.5f));
                Assert.That(root.OpponentDecisionSequence, Is.GreaterThan(1));
                Assert.That(Vector3.Distance(root.Hero.transform.position, root.Opponent.transform.position), Is.LessThan(1.1f),
                    "Observed retreat must make the opponent close in before committing.");
                Quaternion facing = root.Opponent.transform.rotation;
                input.Release(keyboard.sKey, queueEventOnly: true);
                input.Press(keyboard.dKey, queueEventOnly: true);
                input.Press(keyboard.spaceKey, queueEventOnly: true);
                for (int frame = 0; frame < 15; frame++)
                {
                    yield return null;
                    Assert.That(Quaternion.Angle(facing, root.Opponent.transform.rotation), Is.LessThan(.1f),
                        "After committing, the opponent cannot home its swing onto a sidestep.");
                }
                input.Release(keyboard.dKey, queueEventOnly: true);
                input.Release(keyboard.spaceKey, queueEventOnly: true);
                root.AutomaticSimulation = false;
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

        /// <summary>The step's whole defensive value is geometry: taken at the tell it leaves the
        /// authored arc, and the swing that follows finds the whiffer still exposed.</summary>
        [UnityTest]
        public IEnumerator Range_StepsAtTheTellEvadeTheAuthoredArcAndTheStepAttackCountersTheWhiff()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            yield return EnterRange();
            try
            {
                // Evasion is physical: the capsule's physics pose follows the transform only
                // across simulation frames, so this fixture runs on frames, not rules-only ticks.
                // The duel capsule is the honest hurtbox; the wider cloth trigger stays off.
                ConfigureDuelHurtbox(root.Hero);
                ConfigureDuelHurtbox(root.Opponent);
                root.AutomaticSimulation = true;
                float counterStagger = S.StaggerSeconds + S.CounterHitStaggerBonus;
                int reactionFrames = Mathf.RoundToInt(.22f * 60f);

                // A back step opens the gap past the crowbar's reach.
                PlacePair(1.1f);
                Vector3 heroStart = root.Hero.transform.position;
                Assert.That(root.Opponent.TryAttack(), Is.True);
                for (int frame = 0; frame < reactionFrames; frame++) yield return null;
                Assert.That(root.Hero.TryStep(Vector2.down), Is.True);
                for (int frame = 0; frame < 40; frame++) yield return null;
                Assert.That(Vector3.Distance(heroStart, root.Hero.transform.position), Is.EqualTo(S.StepDistance).Within(.05f));
                Assert.That(root.Hero.State.Health, Is.EqualTo(S.MaxHealth), "A back step at the tell leaves the arc.");
                Assert.That(root.Opponent.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Miss));

                // One side step leaves the authored arc; the counter that follows lands on the whiffer.
                string evadedSide = null;
                foreach (Vector2 side in new[] { Vector2.left, Vector2.right })
                {
                    PlacePair(1.1f);
                    Assert.That(root.Opponent.TryAttack(), Is.True);
                    for (int frame = 0; frame < reactionFrames; frame++) yield return null;
                    Assert.That(root.Hero.TryStep(side), Is.True);
                    // Past the arc's end (.63 s) even when a hit-stop froze a few substeps.
                    for (int frame = 0; frame < 30; frame++) yield return null;
                    Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Recovery));
                    if (root.Hero.State.Health < S.MaxHealth) continue;
                    evadedSide = side == Vector2.left ? "left" : "right";
                    Assert.That(root.Opponent.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Miss));
                    // Close the lateral gap and square up the way a player would, then swing
                    // while the whiffer is still recovering.
                    Vector3 toOpponent = root.Opponent.transform.position - root.Hero.transform.position;
                    toOpponent.y = 0f;
                    root.Hero.Body.Move(toOpponent.normalized * Mathf.Max(0f, toOpponent.magnitude - 1.05f));
                    root.Hero.transform.rotation = Quaternion.LookRotation(toOpponent.normalized);
                    Physics.SyncTransforms();
                    yield return null;
                    Assert.That(root.Hero.TryAttack(), Is.True);
                    float before = root.Opponent.State.Health;
                    yield return WaitFor(() => root.Opponent.State.Health < before, "The swing after the evasion must reach the whiffer.");
                    Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Stagger));
                    Assert.That(root.Opponent.State.ActionRemaining, Is.EqualTo(counterStagger).Within(.04f),
                        "Punishing a whiff is a counter-hit.");
                    break;
                }
                Assert.That(evadedSide, Is.Not.Null, "At least one side step must leave the authored arc.");
                Debug.Log("Combat step evasion side: " + evadedSide);
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
            }
        }
    }
}
