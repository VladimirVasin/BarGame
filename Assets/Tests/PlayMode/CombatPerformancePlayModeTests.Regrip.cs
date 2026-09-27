using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatPerformancePlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_ShoveAndSwingRestoreAttackAndBlock()
        {
            for (int rig = 0; rig < 2; rig++)
                for (int action = 0; action < 2; action++)
                {
                    bool shove = action == 0;
                    PlacePair(shove ? .82f : 4f);
                    if (shove && rig == 0)
                    {
                        // Recorded manual stance before the hero's own shove.
                        root.Hero.ResetActor(new Vector3(.1180975f, .0400002f, -2.406575f),
                            new Quaternion(0f, -.08395952f, 0f, .9964692f) * Vector3.forward);
                        root.Opponent.ResetActor(new Vector3(-.1634964f, .0400002f, -1.657811f),
                            new Quaternion(0f, .9941874f, 0f, .107663542f) * Vector3.forward);
                        Physics.SyncTransforms();
                    }
                    for (int frame = 0; frame < 6; frame++)
                    { root.Tick(TickSeconds); yield return new WaitForEndOfFrame(); }
                    CombatActor actor = rig == 0 ? root.Hero : root.Opponent;
                    CombatActor target = rig == 0 ? root.Opponent : root.Hero;
                    string label = (actor.IsHero ? "hero" : "opponent") + (shove ? " shove source" : " swing source");
                    Assert.That(actor.HasTwoHandSupport, Is.True, label + ": initial contact");

                    for (int repetition = 0; repetition < 2; repetition++)
                    {
                        // Reposition only the recipient. Resetting the source
                        // here would erase the permanent post-action grip stall.
                        if (shove && repetition > 0)
                        {
                            target.ResetActor(actor.transform.position + actor.transform.forward * .82f,
                                -actor.transform.forward);
                            Physics.SyncTransforms();
                            root.Tick(TickSeconds);
                            yield return new WaitForEndOfFrame();
                        }
                        int impacts = target.ReceivedImpactCount;
                        int sequence = actor.State.AttackSequence;
                        if (shove)
                            Assert.That(actor.RequestAttack(), Is.True, label + ": repeated command " + repetition);
                        else
                        {
                            // The recorded first swing was a brief held press,
                            // whose charge/release pose seeds the returning arm.
                            Assert.That(actor.RequestCharge(), Is.True, label + ": charge press " + repetition);
                            for (int frame = 0; frame < 6; frame++)
                            { root.Tick(TickSeconds); yield return new WaitForEndOfFrame(); }
                            Assert.That(actor.ReleaseCharge(), Is.True, label + ": release after 0.1 seconds");
                        }
                        Assert.That(actor.State.AttackSequence, Is.Not.EqualTo(sequence), label);
                        Assert.That(shove ? actor.State.IsShoving : actor.State.IsAttacking, Is.True, label);
                        yield return WaitForSourceRegrip(actor, label + " " + repetition);
                        Assert.That(target.ReceivedImpactCount - impacts, Is.EqualTo(shove ? 1 : 0),
                            label + ": the shove must actually land; a separated swing must miss");
                    }

                    // Exercise the public command and its presented guard,
                    // rather than accepting a Ready enum as proof of control.
                    actor.SetBlock(true);
                    Assert.That(actor.State.IsBlocking, Is.True, label + ": block command after repeated actions");
                    for (int frame = 0; frame < 18; frame++)
                    { root.Tick(TickSeconds); yield return new WaitForEndOfFrame(); }
                    Assert.That(actor.State.IsBlocking && actor.HasTwoHandSupport, Is.True,
                        label + ": the raised guard must retain actual contact");
                    actor.SetBlock(false);
                    yield return WaitForSourceRegrip(actor, label + " guard release");

                    target.ResetActor(actor.transform.position + actor.transform.forward * 4f, -actor.transform.forward);
                    Physics.SyncTransforms();
                    Assert.That(actor.RequestAttack(), Is.True, label + ": ordinary attack after block");
                    Assert.That(actor.State.IsAttacking, Is.True, label + ": start the swing, not a queued request or shove");
                    yield return WaitForSourceRegrip(actor, label + " follow-up swing");
                    CaptureDuelFrame("balance/source-regrip/" + (actor.IsHero ? "hero" : "opponent"),
                        shove ? "after-shove-and-block" : "after-swing-and-block");
                }
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator WaitForSourceRegrip(CombatActor actor, string label)
        {
            Transform upper = ArmMotionBone(actor, "upper_arm.L");
            Transform lower = ArmMotionBone(actor, "forearm.L");
            Transform hand = ArmMotionBone(actor, "hand.L");
            Quaternion previousUpper = upper.localRotation, previousLower = lower.localRotation, previousHand = hand.localRotation;
            float readySeconds = 0f, maxReturnJointStep = 0f;
            bool restored = false;
            for (int frame = 0; frame < 240; frame++)
            {
                root.Tick(TickSeconds);
                yield return new WaitForEndOfFrame();
                float jointStep = Mathf.Max(Quaternion.Angle(previousUpper, upper.localRotation),
                    Mathf.Max(Quaternion.Angle(previousLower, lower.localRotation), Quaternion.Angle(previousHand, hand.localRotation)));
                previousUpper = upper.localRotation; previousLower = lower.localRotation; previousHand = hand.localRotation;
                Assert.That(actor.IsKnockedDown || actor.State.IsDefeated, Is.False, label);
                if (actor.State.Phase != MeleePhase.Ready) continue;
                readySeconds += TickSeconds;
                maxReturnJointStep = Mathf.Max(maxReturnJointStep, jointStep);
                if (actor.HasTwoHandSupport) { restored = true; break; }
                if (readySeconds >= 2f) break;
            }
            CombatSupportGrip grip = actor.SupportGrip;
            string diagnostic = $"{label}: phase={actor.State.Phase}, arm={actor.SupportArmState}, wait={grip.JournalGripReason}, " +
                $"gap={grip.JournalContactError:F4}, angle={grip.JournalContactAngle:F2}, wrist={grip.LiveArmAngles}, " +
                $"roll={grip.LiveShoulderRoll:F2}, elbow={grip.LiveSignedElbow:F2}, " +
                $"ready={readySeconds:F3}, jointStep={maxReturnJointStep:F2}";
            Assert.That(restored, Is.True, "Restore physical contact within two seconds of Ready. " + diagnostic);
            Assert.That(maxReturnJointStep, Is.LessThanOrEqualTo(35f), "The returning arm must move continuously. " + diagnostic);
            Assert.That(grip.JournalContactError, Is.LessThanOrEqualTo(.025f), diagnostic);
            Assert.That(grip.JournalContactAngle, Is.LessThanOrEqualTo(12f), diagnostic);
            Assert.That(grip.JournalWristSafe, Is.True, diagnostic);
            Assert.That(grip.LiveArmAngles.x, Is.LessThanOrEqualTo(25.1f), diagnostic);
            Assert.That(grip.LiveArmAngles.y, Is.LessThanOrEqualTo(55.1f), diagnostic);
        }

        [UnityTest]
        public IEnumerator Range_ChargeKeepsGripThroughRecordedCloseStance()
        {
            PlacePair(4f);
            // Last complete sample before manual duel c8ccc's first charge.
            root.Hero.ResetActor(new Vector3(-1.71913815f, .04000006f, -1.41135383f),
                new Quaternion(0f, .705188f, 0f, .709020436f) * Vector3.forward);
            root.Opponent.ResetActor(new Vector3(-.747940361f, .04000018f, -1.46618545f),
                new Quaternion(0f, .922409832f, 0f, -.386212528f) * Vector3.forward);
            Physics.SyncTransforms();
            for (int frame = 0; frame < 6; frame++) { root.Tick(TickSeconds); yield return null; }
            Assert.That(root.Hero.HasTwoHandSupport, Is.True);
            Assert.That(root.Hero.RequestCharge(), Is.True);
            bool lost = false;
            string rejection = null;
            for (int frame = 0; frame < 10; frame++)
            {
                root.Hero.Body.Move(new Vector3(-.014296f, 0f, 1.31878f) * (TickSeconds * root.Hero.MovementScale));
                root.Tick(TickSeconds);
                yield return new WaitForEndOfFrame();
                lost |= !root.Hero.HasTwoHandSupport;
                rejection = root.Hero.SupportGrip.LastPoseRejection ?? rejection;
            }
            CaptureDuelFrame("balance/charge", "nearby-charge");
            TestContext.Out.WriteLine("Recorded close charge: rejected=" + rejection + ", grip=" + root.Hero.SupportArmState);
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero);
            Assert.That(lost, Is.False, "A collision-safe charge must keep its two-hand support: " + rejection);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Range_GripRejectionReleasesFromPresentedArm()
        {
            PlacePair(4f);
            for (int frame = 0; frame < 6; frame++)
            { root.Tick(TickSeconds); yield return new WaitForEndOfFrame(); }
            foreach (CombatActor victim in new[] { root.Hero, root.Opponent })
            {
                Transform shoulder = ArmMotionBone(victim, "upper_arm.L");
                Transform elbow = ArmMotionBone(victim, "forearm.L");
                Transform wrist = ArmMotionBone(victim, "hand.L");
                Quaternion beforeShoulder = shoulder.localRotation, beforeElbow = elbow.localRotation, beforeWrist = wrist.localRotation;
                Assert.That(victim.HasTwoHandSupport, Is.True);
                // An actual occupied palm must reject the contact, including
                // the previous chain, without substituting an authored arm.
                var obstacle = new GameObject("GripPalmObstacle");
                try
                {
                    obstacle.transform.position = victim.SupportGripWorldPosition;
                    obstacle.AddComponent<SphereCollider>().radius = .015f;
                    Physics.SyncTransforms();
                    victim.SupportGrip.Apply();
                    Assert.That(victim.SupportGrip.LastPoseRejection, Is.EqualTo("contact_palm_overlap"));
                }
                finally { Object.DestroyImmediate(obstacle); Physics.SyncTransforms(); }
                Assert.That(victim.SupportArmState, Is.EqualTo(CombatArmSupportState.Releasing));
                Assert.That(victim.SupportGrip.Weight, Is.GreaterThan(.99f));
                Assert.That(Quaternion.Angle(beforeShoulder, shoulder.localRotation), Is.LessThan(.1f));
                Assert.That(Quaternion.Angle(beforeElbow, elbow.localRotation), Is.LessThan(.1f));
                Assert.That(Quaternion.Angle(beforeWrist, wrist.localRotation), Is.LessThan(.1f));
            }
            for (int frame = 0; frame < 8; frame++)
            { root.Tick(TickSeconds); yield return new WaitForEndOfFrame(); }
            foreach (CombatActor victim in new[] { root.Hero, root.Opponent })
            {
                Assert.That(victim.SupportArmState, Is.EqualTo(CombatArmSupportState.Free));
                Assert.That(victim.SupportGrip.Weight, Is.Zero);
                Assert.That(victim.SupportGrip.LiveArmAngles.x, Is.LessThanOrEqualTo(25.1f));
                Assert.That(victim.SupportGrip.LiveArmAngles.y, Is.LessThanOrEqualTo(55.1f));
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Range_WeakShoveKeepsGrip()
        {
            for (int rig = 0; rig < 2; rig++)
            {
                PlacePair(4f);
                for (int frame = 0; frame < 6; frame++)
                { root.Tick(TickSeconds); yield return null; }
                CombatActor victim = rig == 0 ? root.Hero : root.Opponent;
                float health = victim.State.Health;
                PublishShoveReaction(victim, 12f);
                victim.Present();
                Assert.That(victim.State.Phase, Is.EqualTo(MeleePhase.Stagger));
                Assert.That(victim.ImpactMotion.BalanceLoad, Is.LessThan(.35f), "This contact must retain stable support.");
                Assert.That(victim.SupportGrip.IsSupportingWeapon, Is.True,
                    "A non-damaging stagger must not release a physically supported hand through CombatHit.");
                for (int frame = 0; frame < 12; frame++)
                {
                    root.Tick(TickSeconds); yield return null;
                    Assert.That(victim.SupportGrip.IsSupportingWeapon, Is.True, "Weak shove, rig=" + rig);
                    Assert.That(victim.SupportGrip.JournalContactError, Is.LessThanOrEqualTo(.025f));
                    Assert.That(victim.SupportGrip.JournalContactAngle, Is.LessThanOrEqualTo(12f));
                    Assert.That(victim.SupportGrip.JournalWristSafe, Is.True);
                }
                Assert.That(victim.Footwork.CatchStepCount, Is.Zero);

                // Strong release/regrip is covered through actual palm contact
                // and the final anatomical chain in ArmMotion, not target error.
                Assert.That(victim.State.Health, Is.EqualTo(health));
            }
            LogAssert.NoUnexpectedReceived();
        }

        private void PublishShoveReaction(CombatActor victim, float momentum)
        {
            CombatActor source = victim == root.Hero ? root.Opponent : root.Hero;
            Vector3 direction = -victim.transform.forward;
            Vector3 point = victim.Ragdoll.PhysicsController.ChestBody.position - direction * .10f;
            Assert.That(victim.State.ReceiveShove(), Is.True);
            var impact = new CombatImpact(source, victim, source.State.AttackSequence,
                point, -direction, direction, victim.State.Health, victim.State.Health,
                MeleeHitResult.Hit, new MeleeHitLocation(MeleeBodyRegion.Torso, MeleeHitSide.Front),
                part: Player3DAnatomicalPart.Torso, impulse: direction * momentum);
            MethodInfo publish = typeof(CombatActor).GetMethod("PublishImpact", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(publish, Is.Not.Null);
            publish.Invoke(victim, new object[] { impact });
        }

        [UnityTest]
        public IEnumerator Range_FinalCatchRequiresStableMovingSupportBeforeRegrip()
        {
            PlacePair(.82f);
            for (int frame = 0; frame < 6; frame++)
            { root.Tick(TickSeconds); yield return null; }
            CombatActor victim = root.Opponent;
            CombatImpactMotion motion = victim.ImpactMotion;
            Assert.That(motion.ExperimentalRecovery, Is.False);
            Assert.That(root.Hero.RequestAttack(), Is.True);

            // Stop on the visible end of the second swing, before its next
            // simulation step accepts the real presented sole contact.
            bool finalContactPresented = false, gripLost = false;
            for (int tick = 0; tick < 180 && !finalContactPresented; tick++)
            {
                root.Tick(CombatTestRoot.SimulationStep);
                yield return null;
                if (!gripLost && victim.ReceivedImpactCount > 0)
                {
                    // Regrip timing needs a hand that actually lost contact;
                    // a recoverable torso shove alone now keeps its grip.
                    victim.SupportGrip.RejectObstructedPose("contact_world_path");
                    gripLost = true;
                }
                Assert.That(victim.IsKnockedDown, Is.False, DescribeBalance(victim));
                Assert.That(victim.Footwork.CatchStepCount, Is.LessThanOrEqualTo(2));
                finalContactPresented = victim.Footwork.CatchStepCount == 2 &&
                    victim.Footwork.CatchStepActive && victim.Footwork.CatchStepProgress >= 1f;
            }
            Assert.That(finalContactPresented, Is.True, "The real shove must exercise the final catch landing.");
            Assert.That(motion.LandedRecoverySteps, Is.EqualTo(1));
            Assert.That(victim.SupportGrip.IsRegripping, Is.False);
            Assert.That(victim.SupportGrip.JournalRegripAllowed, Is.False);

            Vector3 outward = Vector3.ProjectOnPlane(motion.CentreOfMass - motion.SupportCentre, Vector3.up);
            if (outward.sqrMagnitude < .0001f) outward = victim.transform.forward;
            outward.Normalize();
            float step = CombatTestRoot.SimulationStep;
            Vector3 before = victim.transform.position;
            victim.Body.Move(outward * (2f * step));
            Vector3 achievedVelocity = Vector3.ProjectOnPlane(victim.transform.position - before, Vector3.up) / step;
            Assert.That(achievedVelocity.magnitude, Is.EqualTo(2f).Within(.01f), "Use achieved capsule travel, not a desired speed.");
            // This is the NPC locomotion contract. Advance the actor directly
            // here so the disabled AI does not replace the sample with zero.
            victim.SetLocomotion(achievedVelocity);
            victim.AdvanceSimulation(step);
            victim.Present();

            Assert.That(motion.LandedRecoverySteps, Is.EqualTo(2), DescribeBalance(victim));
            Assert.That(victim.Footwork.CatchStepCount, Is.EqualTo(2));
            Assert.That(victim.Footwork.JournalLeftSupport && victim.Footwork.JournalRightSupport, Is.True);
            Assert.That(motion.HasStableRecoverySupport(Vector3.zero), Is.True,
                "The planted stance itself is stable; ongoing outward movement is the missing condition.");
            Assert.That(motion.HasStableRecoverySupport(achievedVelocity), Is.False);
            Assert.That(victim.Footwork.RecoveryEpisodeActive, Is.True);
            Assert.That(victim.SupportGrip.JournalRegripAllowed, Is.False);
            Assert.That(victim.SupportGrip.IsRegripping, Is.False);

            root.Tick(0f);
            victim.Present(); victim.Present();
            Assert.That(victim.Footwork.RecoveryEpisodeActive, Is.True, "Presentation cannot finish the recovery decision.");
            Assert.That(victim.SupportGrip.JournalRegripAllowed, Is.False);
            Assert.That(motion.LandedRecoverySteps, Is.EqualTo(2));

            victim.SetLocomotion(Vector3.zero);
            victim.AdvanceSimulation(step);
            victim.Present();
            Assert.That(victim.Footwork.RecoveryEpisodeActive, Is.False,
                "Stable support releases the episode on its next decision, without a dwell timer.");
            Assert.That(victim.SupportGrip.JournalRegripAllowed, Is.True);
            Assert.That(victim.IsKnockedDown, Is.False);
            for (int frame = 0; frame < 90 && !victim.HasTwoHandSupport; frame++)
            { root.Tick(TickSeconds); yield return null; }
            Assert.That(victim.HasTwoHandSupport, Is.True, "The hand must actually return to the weapon.");
            Assert.That(victim.Footwork.CatchStepCount, Is.EqualTo(2));
            Assert.That(victim.ReceivedImpactCount, Is.EqualTo(1));
            Assert.That(victim.State.Health, Is.EqualTo(victim.State.Settings.MaxHealth));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
