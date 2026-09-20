using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

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
                        while (root.Hero.State.AttackOutcome != MeleeAttackOutcome.Blocked && contactTicks < 80)
                        { root.Tick(CombatTestRoot.SimulationStep); contactTicks++; }
                        Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.GuardImpact));
                        Assert.That(root.Opponent.TryAttack(), Is.False, "A normal block does not grant an immediate counter.");
                    }
                    root.Tick(Mathf.Max(0f, .7f - contactTicks * CombatTestRoot.SimulationStep));
                    Assert.That(root.Hero.State.AttackOutcome, Is.EqualTo(outcomes[i]), "Actual weapon contact must set recovery.");
                    recoveries[i] = root.Hero.State.RecoveryRemaining;
                    Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Recovery));
                    Assert.That(root.Hero.TryStep(Vector2.down), Is.False, "A committed attack cannot escape into a step.");
                    if (i == 1)
                    {
                        Assert.That(root.Opponent.State.Health, Is.EqualTo(100f));
                    }
                    if (i == 0) Assert.That(root.Opponent.State.Health, Is.EqualTo(75f));
                }
                Assert.That(recoveries[1] - recoveries[0], Is.GreaterThan(.15f));
                Assert.That(recoveries[2] - recoveries[1], Is.GreaterThan(.25f));

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
                    Assert.That(root.Hero.State.Stamina, Is.EqualTo(80f).Within(.001f));
                    Assert.That(root.Hero.Body.enabled, Is.True, "A step keeps the ordinary hittable capsule.");
                    Assert.That(root.Player.Motor.ApplyOwnedDisplacement(new object(), Vector3.forward), Is.EqualTo(Vector3.zero));
                    Assert.That(root.Hero.TryAttack(), Is.False);
                    for (int frame = 0; frame < 45 && root.Hero.State.Phase == MeleePhase.Step; frame++)
                    { yield return null; legs.Sample(); AssertOpponentFramed(root.CameraFollow.Camera); }
                    input.Release(keys[i], queueEventOnly: true);
                    Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
                    Assert.That(Vector3.Dot(root.Hero.transform.position - start, direction), Is.EqualTo(.65f).Within(.045f));
                    legs.AssertMoving(2f, "A short step must move both real legs.");
                    for (int frame = 0; frame < 12; frame++) yield return null;
                    Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready), "Holding Space cannot repeat steps.");
                    Assert.That(root.Hero.State.Stamina, Is.EqualTo(80f).Within(.001f));
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
                Assert.That(root.Hero.State.Stamina, Is.EqualTo(100f));
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
                Assert.That(root.Hero.State.Stamina, Is.EqualTo(80f));
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
                Assert.That(root.Hero.State.Health, Is.EqualTo(75f), "A mistimed step still receives actual weapon contact.");
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
    }
}
