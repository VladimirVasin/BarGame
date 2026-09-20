using System;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CombatRulesTests
    {
        [Test]
        public void CommittedMissCostsStaminaAndCannotCancelIntoAttackOrBlock()
        {
            MeleeCombatant actor = new MeleeCombatant();
            Assert.That(actor.TryStartAttack(), Is.True);
            Assert.That(actor.Stamina, Is.EqualTo(70f));
            actor.SetBlocking(true);
            Assert.That(actor.IsBlocking, Is.False);
            Assert.That(actor.TryStartAttack(), Is.False);
            Assert.That(actor.TryRegisterHit(1, actor.AttackSequence), Is.False);
            actor.Advance(1f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Recovery));
            Assert.That(actor.IsBlocking, Is.False);
            Assert.That(actor.TryStartAttack(), Is.False);
            actor.Advance(0.5f);
            Assert.That(actor.IsBlocking, Is.True);
            Assert.That(actor.Stamina, Is.EqualTo(70f), "A held guard prevents regeneration.");
            actor.SetBlocking(false);
            actor.Advance(0.5f);
            Assert.That(actor.Stamina, Is.EqualTo(81f).Within(0.0001f));
        }

        [Test]
        public void HitchReportsWholeActiveSweepAndTargetCanOnlyBeHitOnce()
        {
            MeleeCombatant actor = new MeleeCombatant();
            actor.TryStartAttack();
            MeleeAdvanceResult step = actor.Advance(2f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(step.HasActiveWindow, Is.True);
            Assert.That(step.ActiveStartNormalized, Is.Zero);
            Assert.That(step.ActiveEndNormalized, Is.EqualTo(1f));
            Assert.That(actor.TryRegisterHit(7, step.AttackSequence), Is.True);
            Assert.That(actor.TryRegisterHit(7, step.AttackSequence), Is.False, "Multiple target colliders share one ID.");
            Assert.That(actor.TryRegisterHit(8, step.AttackSequence), Is.True);
            actor.Advance(0.01f);
            Assert.That(actor.TryRegisterHit(9, step.AttackSequence), Is.False);
            Assert.That(actor.TryStartAttack(), Is.True);
            actor.Advance(0.5f);
            Assert.That(actor.TryRegisterHit(7, step.AttackSequence), Is.False, "Previous swing cannot hit in this one.");
            Assert.That(actor.TryRegisterHit(7, actor.AttackSequence), Is.True);
        }

        [Test]
        public void FineStepsAndHitchHaveSameRecoveryAndStamina()
        {
            MeleeCombatant fine = new MeleeCombatant();
            MeleeCombatant hitch = new MeleeCombatant();
            fine.TryStartAttack();
            hitch.TryStartAttack();
            for (int i = 0; i < 200; i++) fine.Advance(0.01f);
            hitch.Advance(2f);
            Assert.That(fine.Phase, Is.EqualTo(hitch.Phase));
            Assert.That(fine.Stamina, Is.EqualTo(hitch.Stamina).Within(0.0001f));
            Assert.That(hitch.Stamina, Is.EqualTo(82.54f).Within(0.0001f));
        }

        [Test]
        public void FrontalGuardSpendsStaminaThenBreaksWhileRearHitBypassesIt()
        {
            MeleeCombatant actor = new MeleeCombatant();
            actor.SetBlocking(true);
            for (int i = 0; i < 4; i++)
                Assert.That(actor.ReceiveHit(25f, 25f, true), Is.EqualTo(MeleeHitResult.Blocked));
            Assert.That(actor.Health, Is.EqualTo(100f));
            Assert.That(actor.Stamina, Is.Zero);
            Assert.That(actor.ReceiveHit(25f, 25f, true), Is.EqualTo(MeleeHitResult.GuardBroken));
            Assert.That(actor.Health, Is.EqualTo(75f));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.GuardBroken));
            Assert.That(actor.TryStartAttack(), Is.False);
            actor.Advance(0.3f);
            actor.ReceiveHit(5f, 25f, false);
            actor.Advance(0.3f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.GuardBroken), "Another hit cannot shorten guard break.");
            actor.Advance(0.1f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            actor.Reset();
            actor.SetBlocking(true);
            Assert.That(actor.ReceiveHit(25f, 25f, false), Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(actor.Health, Is.EqualTo(75f));
            Assert.That(actor.Stamina, Is.EqualTo(100f));
        }

        [Test]
        public void UnblockedHitInterruptsAttackAndDelaysRegeneration()
        {
            MeleeCombatant actor = new MeleeCombatant();
            actor.TryStartAttack();
            MeleeAdvanceResult step = actor.Advance(0.5f);
            Assert.That(step.HasActiveWindow, Is.True);
            actor.ReceiveHit(25f, 25f, false);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Stagger));
            Assert.That(actor.TryRegisterHit(1, step.AttackSequence), Is.False);
            actor.Advance(0.5f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.Stamina, Is.EqualTo(70f));
            actor.Advance(0.75f);
            Assert.That(actor.Stamina, Is.EqualTo(75.5f).Within(0.0001f));
        }

        [Test]
        public void ExhaustionRejectsAttackAndResetRestoresRoundWithoutStaleHits()
        {
            MeleeCombatant actor = new MeleeCombatant();
            actor.SetBlocking(true);
            for (int i = 0; i < 3; i++)
            {
                Assert.That(actor.TryStartAttack(), Is.True);
                actor.Advance(1.5f);
            }
            Assert.That(actor.Stamina, Is.EqualTo(10f));
            Assert.That(actor.TryStartAttack(), Is.False);
            int obsolete = actor.AttackSequence;
            actor.ReceiveHit(100f, 25f, false);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Defeated));
            Assert.That(actor.Health, Is.Zero);
            Assert.That(actor.TryStartAttack(), Is.False);
            Assert.That(actor.TryStartStep(), Is.False);
            Assert.That(actor.ReceiveHit(25f, 25f, false), Is.EqualTo(MeleeHitResult.Ignored));
            actor.Advance(20f);
            Assert.That(actor.Stamina, Is.EqualTo(10f));
            actor.Reset();
            Assert.That(actor.Health, Is.EqualTo(100f));
            Assert.That(actor.Stamina, Is.EqualTo(100f));
            Assert.That(actor.IsBlocking, Is.False);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            actor.TryStartAttack();
            actor.Advance(0.5f);
            Assert.That(actor.TryRegisterHit(1, obsolete), Is.False);
        }

        [Test]
        public void CancellationPreservesSpentEffortAndCannotReplayTheOldSwing()
        {
            MeleeCombatant actor = new MeleeCombatant();
            actor.TryStartAttack();
            MeleeAdvanceResult step = actor.Advance(0.5f);
            actor.SetBlocking(true);
            actor.CancelAction();
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.Health, Is.EqualTo(100f));
            Assert.That(actor.Stamina, Is.EqualTo(70f));
            Assert.That(actor.IsBlocking, Is.False);
            Assert.That(actor.TryRegisterHit(1, step.AttackSequence), Is.False);
            Assert.That(actor.Advance(0.1f).HasActiveWindow, Is.False);
            actor.ReceiveHit(100f, 25f, false);
            actor.CancelAction();
            Assert.That(actor.IsDefeated, Is.True, "Cancellation cannot resurrect a finished round.");
        }

        [Test]
        public void AttackPressBuffersOnlyNearRecoveryEndAndIsConsumedOnce()
        {
            MeleeCombatant actor = new MeleeCombatant();
            Assert.That(actor.RequestAttack(), Is.True, "An idle press still starts immediately.");
            int firstSwing = actor.AttackSequence;
            Assert.That(actor.RequestAttack(), Is.False, "Windup cannot queue a combo.");
            actor.Advance(actor.Settings.AttackDurationSeconds - .18f);
            Assert.That(actor.RequestAttack(), Is.False, "A press outside the final window is discarded.");
            actor.Advance(.09f);
            actor.SetBlocking(true);
            Assert.That(actor.RequestAttack(), Is.True);
            Assert.That(actor.RequestAttack(), Is.True, "Repeated presses share one pending slot.");
            Assert.That(actor.HasBufferedAttack, Is.True);
            Assert.That(actor.Stamina, Is.EqualTo(70f), "Queueing does not spend before the next attack starts.");
            actor.Advance(.1f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Windup));
            Assert.That(actor.IsBlocking, Is.False, "An explicitly queued attack takes priority over held guard.");
            Assert.That(actor.AttackElapsed, Is.EqualTo(.01f).Within(.00001f));
            Assert.That(actor.AttackSequence, Is.EqualTo(firstSwing + 1));
            Assert.That(actor.Stamina, Is.EqualTo(40f));
            Assert.That(actor.HasBufferedAttack, Is.False);
            actor.Advance(2f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.AttackSequence, Is.EqualTo(firstSwing + 1), "One press never repeats itself.");

            var exhausted = new MeleeCombatant(new MeleeCombatSettings(attackCost: 60f));
            exhausted.TryStartAttack();
            exhausted.Advance(exhausted.Settings.AttackDurationSeconds - .08f);
            Assert.That(exhausted.RequestAttack(), Is.False);
            Assert.That(exhausted.HasBufferedAttack, Is.False,
                "An unaffordable press must not become an attack after a later regeneration.");
        }

        [Test]
        public void BufferedAttackStartsAtRecoveryBoundaryEvenWhenHitchCrossesItsActivePhase()
        {
            MeleeCombatant fine = new MeleeCombatant();
            MeleeCombatant hitch = new MeleeCombatant();
            foreach (MeleeCombatant actor in new[] { fine, hitch })
            {
                actor.TryStartAttack();
                actor.Advance(actor.Settings.AttackDurationSeconds - .08f);
                Assert.That(actor.RequestAttack(), Is.True);
            }
            int nextSwing = hitch.AttackSequence + 1;
            for (int i = 0; i < 200; i++) fine.Advance(.01f);
            MeleeAdvanceResult window = hitch.Advance(2f);
            Assert.That(hitch.AttackSequence, Is.EqualTo(nextSwing));
            Assert.That(window.AttackSequence, Is.EqualTo(nextSwing));
            Assert.That(window.ActiveStartNormalized, Is.Zero);
            Assert.That(window.ActiveEndNormalized, Is.EqualTo(1f));
            Assert.That(hitch.TryRegisterHit(7, window.AttackSequence), Is.True);
            Assert.That(hitch.TryRegisterHit(7, window.AttackSequence), Is.False);
            Assert.That(hitch.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(fine.Phase, Is.EqualTo(hitch.Phase));
            Assert.That(fine.AttackSequence, Is.EqualTo(hitch.AttackSequence));
            Assert.That(fine.AttackElapsed, Is.EqualTo(hitch.AttackElapsed).Within(.00001f));
            Assert.That(fine.Stamina, Is.EqualTo(hitch.Stamina).Within(.0001f));
            Assert.That(hitch.Stamina, Is.EqualTo(50.78f).Within(.0001f));
        }

        [TestCase("hit", false)]
        [TestCase("defeat", false)]
        [TestCase("reset", false)]
        [TestCase("cancel", false)]
        [TestCase("hit", true)]
        [TestCase("defeat", true)]
        [TestCase("reset", true)]
        [TestCase("cancel", true)]
        [TestCase("guard", true)]
        [TestCase("step", true)]
        [TestCase("chargeCancel", true)]
        public void InterruptedBufferedPressCannotReplayAfterRelease(string interruption, bool charge)
        {
            MeleeCombatant actor = new MeleeCombatant();
            actor.TryStartAttack();
            actor.Advance(actor.Settings.AttackDurationSeconds - .08f);
            Assert.That(charge ? actor.RequestCharge() : actor.RequestAttack(), Is.True);
            if (charge) Assert.That(actor.ReleaseCharge(), Is.True, "An already released queued tap must also be cancelled.");
            switch (interruption)
            {
                case "hit": actor.ReceiveHit(10f, 25f, false); break;
                case "defeat": actor.ReceiveHit(100f, 25f, false); break;
                case "reset": actor.Reset(); break;
                case "cancel": actor.CancelAction(); break;
                case "guard": actor.SetBlocking(true); break;
                case "step": actor.TryStartStep(); break;
                case "chargeCancel": actor.CancelCharge(); break;
            }
            Assert.That(actor.HasBufferedAttack, Is.False);
            Assert.That(actor.ReleaseCharge(), Is.False);
            int sequence = actor.AttackSequence;
            Assert.That(actor.Advance(2f).HasActiveWindow, Is.False);
            Assert.That(actor.AttackSequence, Is.EqualTo(sequence));
            Assert.That(actor.IsAttacking, Is.False);
        }

        [TestCase(.2f)]
        [TestCase(.5f)]
        [TestCase(1.35f)]
        [TestCase(2f)]
        public void ObstacleEndsCurrentOrJustCrossedSwingIntoOneFullRecovery(float elapsed)
        {
            MeleeCombatant actor = new MeleeCombatant();
            actor.TryStartAttack();
            actor.Advance(elapsed);
            if (actor.Phase == MeleePhase.Recovery && actor.RecoveryRemaining <= actor.Settings.AttackBufferSeconds)
                Assert.That(actor.RequestAttack(), Is.True);
            float stamina = actor.Stamina;
            int sequence = actor.AttackSequence;
            Assert.That(actor.CancelAttackOnObstacle(), Is.True);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Recovery));
            Assert.That(actor.PhaseProgress, Is.EqualTo(0f).Within(.00001f));
            Assert.That(actor.RecoveryRemaining, Is.EqualTo(actor.Settings.ObstacleRecoverySeconds).Within(.00001f));
            Assert.That(actor.Stamina, Is.EqualTo(stamina));
            Assert.That(actor.HasBufferedAttack, Is.False);
            Assert.That(actor.TryRegisterHit(7, sequence), Is.False);
            Assert.That(actor.Advance(.2f).HasActiveWindow, Is.False);
            Assert.That(actor.CancelAttackOnObstacle(), Is.False, "Repeated wall overlaps cannot extend recovery.");
            Assert.That(actor.RecoveryRemaining, Is.EqualTo(.45f).Within(.00001f));
            actor.Advance(.46f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.CancelAttackOnObstacle(), Is.False);
        }

        [TestCase(MeleeAttackOutcome.Hit, .32f, 0f)]
        [TestCase(MeleeAttackOutcome.Blocked, .50f, 0f)]
        [TestCase(MeleeAttackOutcome.Miss, .80f, 0f)]
        [TestCase(MeleeAttackOutcome.Obstacle, .65f, 0f)]
        [TestCase(MeleeAttackOutcome.Hit, .48f, 1f)]
        [TestCase(MeleeAttackOutcome.Blocked, .75f, 1f)]
        [TestCase(MeleeAttackOutcome.Miss, 1.20f, 1f)]
        [TestCase(MeleeAttackOutcome.Obstacle, .975f, 1f)]
        public void ContactOutcomeOwnsRecoveryButKeepsTheAuthoredAnimationEndpoint(MeleeAttackOutcome outcome, float recovery, float power)
        {
            var actor = new MeleeCombatant();
            if (power == 0f) actor.TryStartAttack();
            else
            {
                actor.RequestCharge();
                actor.Advance(actor.Settings.ChargeSeconds * power);
                actor.ReleaseCharge();
            }
            float contactTime = actor.AttackWindupSeconds + actor.Settings.ActiveSeconds * .4f;
            actor.Advance(contactTime);
            if (outcome == MeleeAttackOutcome.Obstacle)
                Assert.That(actor.CancelAttackOnObstacle(), Is.True);
            else
            {
                if (outcome != MeleeAttackOutcome.Miss)
                {
                    Assert.That(actor.TryRegisterHit(1, actor.AttackSequence), Is.True);
                    Assert.That(actor.RecordAttackOutcome(outcome == MeleeAttackOutcome.Hit
                        ? MeleeHitResult.Hit : MeleeHitResult.Blocked, actor.AttackSequence), Is.True);
                }
                actor.Advance(actor.AttackActiveEnd - contactTime + .000001f);
            }
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Recovery));
            Assert.That(actor.AttackOutcome, Is.EqualTo(outcome));
            Assert.That(actor.RecoveryRemaining, Is.EqualTo(recovery).Within(.00001f));
            Assert.That(actor.CurrentAttackDurationSeconds, Is.EqualTo(actor.AttackActiveEnd + recovery).Within(.00001f));
            Assert.That(actor.Settings.AnimationAttackDurationSeconds, Is.EqualTo(1.28f).Within(.00001f));
            actor.Advance(recovery * .5f);
            Assert.That(actor.PhaseProgress, Is.EqualTo(.5f).Within(.00001f));
            Assert.That(actor.AttackProgress, Is.EqualTo((.63f + .325f) / 1.28f).Within(.00001f),
                "All gameplay outcomes traverse the same authored recovery pose range.");
            actor.Advance(recovery * .5f + .000001f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.AttackProgress, Is.EqualTo(1f).Within(.00001f));
            Assert.That(actor.AttackOutcome, Is.EqualTo(outcome), "The AI may observe the completed result until the next action.");
        }

        [TestCase(MeleeHitResult.Hit)]
        [TestCase(MeleeHitResult.GuardBroken)]
        public void DamageOutcomeOutranksBlocksAndSurvivesRegisteredSimultaneousInterruption(MeleeHitResult result)
        {
            var actor = new MeleeCombatant();
            actor.TryStartAttack();
            actor.Advance(.5f);
            int sequence = actor.AttackSequence;
            actor.TryRegisterHit(1, sequence);
            Assert.That(actor.RecordAttackOutcome(MeleeHitResult.Blocked, sequence), Is.True);
            Assert.That(actor.RecordAttackOutcome(result, sequence), Is.True);
            Assert.That(actor.RecordAttackOutcome(MeleeHitResult.Blocked, sequence), Is.True);
            Assert.That(actor.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Hit));
            Assert.That(actor.AttackRecoverySeconds, Is.EqualTo(.32f));
            Assert.That(actor.RecordAttackOutcome(result, sequence - 1), Is.False);

            foreach (float damage in new[] { 25f, 100f })
            {
                actor.Reset();
                actor.TryStartAttack();
                actor.Advance(.5f);
                sequence = actor.AttackSequence;
                actor.TryRegisterHit(1, sequence);
                actor.ReceiveHit(damage, 25f, false);
                MeleePhase interrupted = actor.Phase;
                Assert.That(actor.RecordAttackOutcome(result, sequence), Is.True,
                    "Collect-before-apply preserves the source's contact even if another contact interrupts it first.");
                Assert.That(actor.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Hit));
                Assert.That(actor.Phase, Is.EqualTo(interrupted), "Recording an outgoing result never revives the interrupted action.");
                actor.Advance(.01f);
                Assert.That(actor.RecordAttackOutcome(result, sequence), Is.False, "A collected window expires at the next simulation step.");
            }
        }

        [TestCase(MeleeHitResult.Hit, 92f)]
        [TestCase(MeleeHitResult.Blocked, 89.14f)]
        public void LateHitchOutcomePreservesTheSameReadyBoundaryAndRegeneration(MeleeHitResult result, float expectedStamina)
        {
            var fine = new MeleeCombatant();
            var hitch = new MeleeCombatant();
            fine.TryStartAttack();
            hitch.TryStartAttack();
            fine.Advance(.5f);
            fine.TryRegisterHit(1, fine.AttackSequence);
            fine.RecordAttackOutcome(result, fine.AttackSequence);
            fine.Advance(1.5f);
            hitch.Advance(2f);
            hitch.TryRegisterHit(1, hitch.AttackSequence);
            Assert.That(hitch.RecordAttackOutcome(result, hitch.AttackSequence), Is.True);
            Assert.That(hitch.Phase, Is.EqualTo(fine.Phase));
            Assert.That(hitch.AttackElapsed, Is.EqualTo(fine.AttackElapsed).Within(.00001f));
            Assert.That(hitch.Stamina, Is.EqualTo(fine.Stamina).Within(.0001f));
            Assert.That(hitch.Stamina, Is.EqualTo(expectedStamina).Within(.0001f));
        }

        [Test]
        public void SuccessfulGuardCommitsBrieflyButStillProtectsAgainstFrontalFollowups()
        {
            var actor = new MeleeCombatant();
            actor.SetBlocking(true);
            Assert.That(actor.ReceiveHit(25f, 25f, true), Is.EqualTo(MeleeHitResult.Blocked));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.GuardImpact));
            Assert.That(actor.IsBlocking, Is.True);
            Assert.That(actor.TryStartAttack(), Is.False);
            Assert.That(actor.RequestAttack(), Is.False);
            Assert.That(actor.TryStartStep(), Is.False);
            actor.Advance(.1f);
            Assert.That(actor.ReceiveHit(25f, 25f, true), Is.EqualTo(MeleeHitResult.Blocked));
            Assert.That(actor.Health, Is.EqualTo(100f));
            Assert.That(actor.Stamina, Is.EqualTo(50f));
            actor.Advance(.11f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.GuardImpact), "The second impact has its own short commitment.");
            actor.SetBlocking(false);
            Assert.That(actor.IsBlocking, Is.False, "Guard impact does not protect an explicitly released guard.");
            actor.Advance(.1f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.TryStartAttack(), Is.True);
        }

        [Test]
        public void DefensiveStepSeparatesTravelFromRecoveryAndRetainsItsFinalTravelSample()
        {
            var actor = new MeleeCombatant();
            actor.SetBlocking(true);
            Assert.That(actor.TryStartStep(), Is.True);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Step));
            Assert.That(actor.Stamina, Is.EqualTo(80f));
            actor.SetBlocking(true);
            Assert.That(actor.IsBlocking, Is.False);
            Assert.That(actor.TryStartAttack(), Is.False);
            Assert.That(actor.RequestAttack(), Is.False);
            Assert.That(actor.TryStartStep(), Is.False);
            Assert.That(actor.HasBufferedAttack, Is.False);
            actor.Advance(.15f);
            Assert.That(actor.StepTravelProgress, Is.EqualTo(.5f).Within(.00001f));
            actor.Advance(.16f);
            Assert.That(actor.StepTravelProgress, Is.EqualTo(1f));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Step), "Finishing travel does not skip the planted recovery.");
            actor.Advance(.16f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.StepElapsed, Is.EqualTo(.46f).Within(.00001f));
            Assert.That(actor.StepTravelProgress, Is.EqualTo(1f), "Runtime must still integrate the last crossed travel interval.");
            Assert.That(actor.StepProgress, Is.EqualTo(1f));
            Assert.That(actor.Stamina, Is.EqualTo(80f));
            Assert.That(actor.IsBlocking, Is.True, "A still-held guard resumes only after the complete step.");
            actor.TryStartAttack();
            Assert.That(actor.StepElapsed, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ADefensiveStepHasNoInvulnerabilityOrGuard(bool fromFront)
        {
            var actor = new MeleeCombatant();
            actor.TryStartStep();
            actor.SetBlocking(true);
            actor.Advance(.12f);
            Assert.That(actor.ReceiveHit(25f, 25f, fromFront), Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(actor.Health, Is.EqualTo(75f));
            Assert.That(actor.Stamina, Is.EqualTo(80f));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Stagger));
            Assert.That(actor.StepElapsed, Is.Zero);
            Assert.That(actor.HasBufferedAttack, Is.False);
        }

        [Test]
        public void StepEffortRegenerationAndResetDoNotDependOnFramePartition()
        {
            var fine = new MeleeCombatant();
            var hitch = new MeleeCombatant();
            fine.TryStartStep();
            hitch.TryStartStep();
            for (int i = 0; i < 120; i++) fine.Advance(.01f);
            hitch.Advance(1.2f);
            Assert.That(hitch.Stamina, Is.EqualTo(84.4f).Within(.0001f));
            Assert.That(fine.Stamina, Is.EqualTo(hitch.Stamina).Within(.0001f));
            Assert.That(hitch.StepElapsed, Is.EqualTo(fine.StepElapsed));
            Assert.That(hitch.Phase, Is.EqualTo(MeleePhase.Ready));
            var exhausted = new MeleeCombatant(new MeleeCombatSettings(stepCost: 60f));
            exhausted.TryStartStep();
            exhausted.Advance(.5f);
            Assert.That(exhausted.TryStartStep(), Is.False);
            exhausted.Reset();
            Assert.That(exhausted.StepElapsed, Is.Zero);
            Assert.That(exhausted.Stamina, Is.EqualTo(100f));
            Assert.That(exhausted.TryStartStep(), Is.True);
            exhausted.CancelAction();
            Assert.That(exhausted.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(exhausted.StepElapsed, Is.Zero);
            Assert.That(exhausted.Stamina, Is.EqualTo(40f), "Cancellation does not refund defensive movement.");
        }

        [TestCase(0f, 0f, 25f, 70f, .45f)]
        [TestCase(.45f, .5f, 32.5f, 62.5f, .275f)]
        [TestCase(.9f, 1f, 40f, 55f, .10f)]
        [TestCase(3f, 1f, 40f, 55f, .10f)]
        public void ChargeReleaseLatchesAffordablePowerAndTraversesTheAuthoredArc(
            float heldSeconds, float power, float damage, float stamina, float windup)
        {
            var actor = new MeleeCombatant();
            Assert.That(actor.RequestCharge(), Is.True);
            int sequence = actor.AttackSequence;
            Assert.That(actor.Stamina, Is.EqualTo(70f), "The base commitment is paid before release.");
            Assert.That(actor.ChargeLimit01, Is.EqualTo(1f));
            Assert.That(actor.Advance(heldSeconds).HasActiveWindow, Is.False);
            Assert.That(actor.IsCharging, Is.True, "Full charge waits for release, even through a hitch.");
            Assert.That(actor.IsAttacking, Is.False);
            Assert.That(actor.Charge01, Is.EqualTo(power).Within(.00001f));
            Assert.That(actor.Stamina, Is.EqualTo(stamina).Within(.0001f));
            Assert.That(actor.TryRegisterHit(1, sequence), Is.False, "A held weapon cannot damage a target.");
            Assert.That(actor.RequestCharge(), Is.True);
            Assert.That(actor.Stamina, Is.EqualTo(stamina).Within(.0001f), "Repeated holds cannot repay the base cost.");
            Assert.That(actor.ReleaseCharge(), Is.True);
            Assert.That(actor.AttackSequence, Is.EqualTo(sequence));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Windup));
            Assert.That(actor.Charge01, Is.Zero);
            Assert.That(actor.AttackPower, Is.EqualTo(power).Within(.00001f));
            Assert.That(actor.AttackDamage, Is.EqualTo(damage).Within(.0001f));
            Assert.That(actor.AttackBlockCost, Is.EqualTo(damage).Within(.0001f));
            Assert.That(actor.AttackWindupSeconds, Is.EqualTo(windup).Within(.00001f));
            Assert.That(actor.CancelCharge(), Is.False, "Release commits an ordinary swing, which cannot be cancelled as charge.");
            Assert.That(actor.AnimationProgressAt(windup * .5f), Is.EqualTo(.225f / 1.28f).Within(.00001f));
            Assert.That(actor.AnimationProgressAt(windup + .09f), Is.EqualTo(.54f / 1.28f).Within(.00001f));
            Assert.That(actor.AnimationProgressAt(actor.AttackActiveEnd), Is.EqualTo(.63f / 1.28f).Within(.00001f));
            Assert.That(actor.AnimationProgressAt(actor.AttackActiveEnd + actor.AttackRecoverySeconds * .5f),
                Is.EqualTo(.955f / 1.28f).Within(.00001f));
            actor.Advance(actor.CurrentAttackDurationSeconds + .00001f);
            Assert.That(actor.AttackProgress, Is.EqualTo(1f));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.TryStartAttack(), Is.True, "The legacy AI/authoring entry point still bypasses charge.");
            Assert.That(actor.AttackPower, Is.Zero);
            Assert.That(actor.AttackDamage, Is.EqualTo(25f));
            Assert.That(actor.AttackWindupSeconds, Is.EqualTo(.45f));
        }

        [TestCase(30f, 0f, 25f)]
        [TestCase(37.5f, .5f, 32.5f)]
        [TestCase(45f, 1f, 40f)]
        public void LimitedStaminaCapsChargeWithoutRegenerationOrAutomaticRelease(float available, float limit, float damage)
        {
            var actor = new MeleeCombatant(new MeleeCombatSettings(maxStamina: available));
            Assert.That(actor.RequestCharge(), Is.True);
            Assert.That(actor.ChargeLimit01, Is.EqualTo(limit));
            Assert.That(actor.Advance(10f).HasActiveWindow, Is.False);
            Assert.That(actor.Charge01, Is.EqualTo(limit));
            Assert.That(actor.Stamina, Is.Zero.Within(.0001f));
            Assert.That(actor.IsCharging, Is.True);
            actor.Advance(10f);
            Assert.That(actor.Stamina, Is.Zero.Within(.0001f), "A held cap must neither drain below zero nor regenerate.");
            Assert.That(actor.ReleaseCharge(), Is.True);
            Assert.That(actor.AttackDamage, Is.EqualTo(damage));

            var exhausted = new MeleeCombatant(new MeleeCombatSettings(stepCost: 80f));
            exhausted.TryStartStep();
            exhausted.Advance(.5f);
            Assert.That(exhausted.RequestCharge(), Is.False, "Less than the base commitment cannot reserve charge.");
            Assert.That(exhausted.Stamina, Is.EqualTo(20f));
            Assert.That(exhausted.HasBufferedAttack, Is.False);
        }

        [TestCase(30)]
        [TestCase(90)]
        [TestCase(300)]
        public void ChargeCostReleaseWindowAndRecoveryAreIndependentOfFramePartition(int ticks)
        {
            var fine = new MeleeCombatant();
            var hitch = new MeleeCombatant();
            fine.RequestCharge();
            hitch.RequestCharge();
            for (int i = 0; i < ticks; i++) Assert.That(fine.Advance(.01f).HasActiveWindow, Is.False);
            Assert.That(hitch.Advance(ticks * .01f).HasActiveWindow, Is.False);
            Assert.That(hitch.Charge01, Is.EqualTo(fine.Charge01).Within(.00001f));
            Assert.That(hitch.Stamina, Is.EqualTo(fine.Stamina).Within(.0001f));
            Assert.That(fine.ReleaseCharge() && hitch.ReleaseCharge(), Is.True);
            for (int i = 0; i < 240; i++) fine.Advance(.01f);
            MeleeAdvanceResult swept = hitch.Advance(2.4f);
            Assert.That(swept.ActiveStartNormalized, Is.Zero);
            Assert.That(swept.ActiveEndNormalized, Is.EqualTo(1f));
            Assert.That(hitch.TryRegisterHit(7, swept.AttackSequence), Is.True);
            Assert.That(hitch.TryRegisterHit(7, swept.AttackSequence), Is.False);
            Assert.That(hitch.Phase, Is.EqualTo(fine.Phase));
            Assert.That(hitch.AttackPower, Is.EqualTo(fine.AttackPower).Within(.00001f));
            Assert.That(hitch.AttackElapsed, Is.EqualTo(fine.AttackElapsed).Within(.00001f));
            Assert.That(hitch.Stamina, Is.EqualTo(fine.Stamina).Within(.0001f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BufferedChargeBeginsAtReadyAndAReleasedQueueBecomesOneTap(bool releasedBeforeReady)
        {
            var actor = new MeleeCombatant();
            actor.TryStartAttack();
            int previous = actor.AttackSequence;
            actor.Advance(actor.CurrentAttackDurationSeconds - .2f);
            Assert.That(actor.RequestCharge(), Is.False);
            actor.Advance(.1f);
            Assert.That(actor.RequestCharge(), Is.True);
            if (releasedBeforeReady) Assert.That(actor.ReleaseCharge(), Is.True);
            Assert.That(actor.HasBufferedCharge, Is.True);
            Assert.That(actor.Stamina, Is.EqualTo(70f));
            actor.Advance(.05f);
            Assert.That(actor.IsCharging, Is.False);
            Assert.That(actor.Charge01, Is.Zero, "Recovery time cannot contribute to the next charge.");
            actor.Advance(.15f);
            Assert.That(actor.AttackSequence, Is.EqualTo(previous + 1));
            Assert.That(actor.HasBufferedAttack, Is.False);
            if (releasedBeforeReady)
            {
                Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Windup));
                Assert.That(actor.AttackElapsed, Is.EqualTo(.1f).Within(.00001f));
                Assert.That(actor.AttackPower, Is.Zero);
                Assert.That(actor.Stamina, Is.EqualTo(40f));
                Assert.That(actor.ReleaseCharge(), Is.False);
            }
            else
            {
                Assert.That(actor.IsCharging, Is.True);
                Assert.That(actor.Charge01, Is.EqualTo(.1f / .9f).Within(.00001f));
                Assert.That(actor.ReleaseCharge(), Is.True);
            }
            actor.Advance(3f);
            Assert.That(actor.AttackSequence, Is.EqualTo(previous + 1), "One buffered press must not repeat itself.");
            Assert.That(actor.IsCharging || actor.IsAttacking, Is.False);
        }

        [TestCase("cancel", 62.5f)]
        [TestCase("guard", 62.5f)]
        [TestCase("step", 42.5f)]
        [TestCase("hit", 62.5f)]
        [TestCase("defeat", 62.5f)]
        [TestCase("owner", 62.5f)]
        [TestCase("reset", 100f)]
        public void ChargingCancellationKeepsSpentEffortAndCannotReplayOnRelease(string interruption, float stamina)
        {
            var actor = new MeleeCombatant();
            actor.RequestCharge();
            actor.Advance(.45f);
            switch (interruption)
            {
                case "cancel": Assert.That(actor.CancelCharge(), Is.True); break;
                case "guard": actor.SetBlocking(true); Assert.That(actor.IsBlocking, Is.True); break;
                case "step": Assert.That(actor.TryStartStep(), Is.True); break;
                case "hit": actor.ReceiveHit(10f, 25f, false); break;
                case "defeat": actor.ReceiveHit(100f, 25f, false); break;
                case "owner": actor.CancelAction(); break;
                case "reset": actor.Reset(); break;
            }
            Assert.That(actor.Stamina, Is.EqualTo(stamina).Within(.0001f));
            Assert.That(actor.IsCharging || actor.HasBufferedAttack, Is.False);
            Assert.That(actor.Charge01, Is.Zero);
            Assert.That(actor.ChargeLimit01, Is.Zero);
            Assert.That(actor.ReleaseCharge(), Is.False);
            int sequence = actor.AttackSequence;
            Assert.That(actor.Advance(5f).HasActiveWindow, Is.False);
            Assert.That(actor.AttackSequence, Is.EqualTo(sequence));
        }

        [TestCase(25f)]
        [TestCase(100f)]
        public void ARegisteredChargedContactRetainsPowerAcrossSimultaneousReceivedDamage(float damage)
        {
            var actor = new MeleeCombatant();
            actor.RequestCharge();
            actor.Advance(.9f);
            actor.ReleaseCharge();
            actor.Advance(.12f);
            int sequence = actor.AttackSequence;
            Assert.That(actor.TryRegisterHit(3, sequence), Is.True);
            actor.ReceiveHit(damage, 25f, false);
            MeleePhase interrupted = actor.Phase;
            Assert.That(actor.AttackPower, Is.EqualTo(1f));
            Assert.That(actor.AttackDamage, Is.EqualTo(40f));
            Assert.That(actor.AttackBlockCost, Is.EqualTo(40f));
            Assert.That(actor.RecordAttackOutcome(MeleeHitResult.Hit, sequence), Is.True);
            Assert.That(actor.Phase, Is.EqualTo(interrupted));
            actor.Reset();
            Assert.That(actor.AttackPower, Is.Zero);
            Assert.That(actor.AttackDamage, Is.EqualTo(25f));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidOutcomeAndStepTuningCannotCreateBrokenClocks(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(hitRecoverySeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(blockRecoverySeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(obstacleRecoverySeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(guardImpactSeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(stepCost: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(stepTravelSeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(stepRecoverySeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(stepDistance: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(animationRecoverySeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(chargeSeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(chargeDamageBonus: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(chargeStaminaCost: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(chargeBlockCostBonus: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(chargedWindupSeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(chargeRecoveryBonus: value));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidTimeCannotCorruptState(float seconds)
        {
            MeleeCombatant actor = new MeleeCombatant();
            Assert.Throws<ArgumentOutOfRangeException>(() => actor.Advance(seconds));
            Assert.Throws<ArgumentOutOfRangeException>(() => actor.AnimationProgressAt(seconds));
            Assert.That(actor.Stamina, Is.EqualTo(100f));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
        }
    }
}
