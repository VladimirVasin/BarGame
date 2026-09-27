using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatPerformancePlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_ShoveRecoveryKeepsHeldWalkingWithItsFeet()
        {
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                PlacePair(.82f);
                for (int frame = 0; frame < 6; frame++)
                { root.Tick(TickSeconds); yield return null; }
                CombatActor victim = root.Hero;
                CombatFootwork feet = victim.Footwork;
                CombatImpactMotion motion = victim.ImpactMotion;
                Assert.That(motion.ExperimentalRecovery, Is.False);
                Assert.That(root.Opponent.RequestAttack(), Is.True);
                for (int frame = 0; frame < 60 && !feet.CatchStepActive; frame++)
                { root.Tick(TickSeconds); yield return null; }
                Assert.That(victim.ReceivedImpactCount, Is.EqualTo(1));
                Assert.That(victim.LastImpact.Impulse.magnitude, Is.EqualTo(165f).Within(.001f));
                Assert.That(feet.CatchStepActive, Is.True, "The actual shove must exercise rescue footwork.");

                int sequence = motion.RecoverySequence;
                Vector3 pushedFrom = victim.transform.position;
                float pushedTravel = 0f;
                bool slowCatch = false, released = false;
                input.Press(keyboard.wKey, queueEventOnly: true);
                for (int frame = 0; frame < 120 && !released; frame++)
                {
                    root.Tick(TickSeconds);
                    bool ownsFeet = feet.RecoveryEpisodeActive;
                    if (ownsFeet)
                    {
                        Assert.That(victim.MovementScale, Is.Zero, DescribeBalance(victim));
                        Assert.That(victim.TurnScale, Is.Zero);
                        slowCatch |= motion.Velocity.sqrMagnitude <= .04f;
                    }
                    yield return null;
                    Assert.That(victim.IsKnockedDown, Is.False, DescribeBalance(victim));
                    Assert.That(feet.CatchStepCount, Is.LessThanOrEqualTo(2));
                    if (ownsFeet)
                    {
                        Assert.That(root.Player.Motor.PlanarVelocity.magnitude, Is.LessThan(.001f),
                            "Held walking may not move the capsule away from a rescue foot's fixed target.");
                        pushedTravel = Mathf.Max(pushedTravel,
                            Vector3.ProjectOnPlane(victim.transform.position - pushedFrom, Vector3.up).magnitude);
                    }
                    released = !feet.RecoveryEpisodeActive && victim.MovementScale > .99f;
                }
                Assert.That(slowCatch, Is.True, "Exercise the interval that the old velocity-only gate missed.");
                Assert.That(pushedTravel, Is.GreaterThan(.05f), "The input gate must preserve physical impulse travel.");
                Assert.That(released, Is.True, "Stable support must return walking without waiting indefinitely.");
                Assert.That(feet.CatchLandingCount, Is.EqualTo(feet.CatchStepCount));
                int completedSteps = feet.CatchStepCount;
                Vector3 walkedFrom = victim.transform.position;
                bool ordinaryStep = false, immediateGait = false;
                float walkingSpeed = 0f;
                for (int frame = 0; frame < 24; frame++)
                {
                    root.Tick(TickSeconds);
                    bool ordinaryTransfer = feet.TransferringFoot && !feet.CatchStepActive;
                    ordinaryStep |= ordinaryTransfer;
                    immediateGait |= ordinaryTransfer && motion.IsActive;
                    yield return null;
                    walkingSpeed = Mathf.Max(walkingSpeed, root.Player.Motor.PlanarVelocity.magnitude);
                    Assert.That(feet.RecoveryEpisodeActive, Is.False, "Ordinary walking cannot reopen the completed episode.");
                    Assert.That(feet.CatchStepCount, Is.EqualTo(completedSteps), "The same impulse gets no extra rescue steps.");
                    Assert.That(motion.RecoverySequence, Is.EqualTo(sequence));
                    Assert.That(victim.IsKnockedDown, Is.False, DescribeBalance(victim));
                }
                Assert.That(walkingSpeed, Is.GreaterThan(.5f));
                Assert.That(Vector3.ProjectOnPlane(victim.transform.position - walkedFrom, Vector3.up).magnitude,
                    Is.GreaterThan(.1f));
                Assert.That(ordinaryStep && immediateGait, Is.True,
                    "Feet must resume their ordinary gait with the motor, while the old impact is still settling.");
                Assert.That(victim.ReceivedImpactCount, Is.EqualTo(1));
                Assert.That(victim.State.Health, Is.EqualTo(victim.State.Settings.MaxHealth));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }
    }
}
