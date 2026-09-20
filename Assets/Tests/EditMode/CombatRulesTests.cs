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

        [TestCase("hit")]
        [TestCase("defeat")]
        [TestCase("reset")]
        [TestCase("cancel")]
        public void InterruptedBufferedPressCannotReplayAfterRelease(string interruption)
        {
            MeleeCombatant actor = new MeleeCombatant();
            actor.TryStartAttack();
            actor.Advance(actor.Settings.AttackDurationSeconds - .08f);
            Assert.That(actor.RequestAttack(), Is.True);
            switch (interruption)
            {
                case "hit": actor.ReceiveHit(10f, 25f, false); break;
                case "defeat": actor.ReceiveHit(100f, 25f, false); break;
                case "reset": actor.Reset(); break;
                case "cancel": actor.CancelAction(); break;
            }
            Assert.That(actor.HasBufferedAttack, Is.False);
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

        [TestCase(MeleeAttackOutcome.Hit, .32f)]
        [TestCase(MeleeAttackOutcome.Blocked, .50f)]
        [TestCase(MeleeAttackOutcome.Miss, .80f)]
        [TestCase(MeleeAttackOutcome.Obstacle, .65f)]
        public void ContactOutcomeOwnsRecoveryButKeepsTheAuthoredAnimationEndpoint(MeleeAttackOutcome outcome, float recovery)
        {
            var actor = new MeleeCombatant();
            actor.TryStartAttack();
            actor.Advance(.5f);
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
                actor.Advance(actor.Settings.WindupSeconds + actor.Settings.ActiveSeconds - .5f);
            }
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Recovery));
            Assert.That(actor.AttackOutcome, Is.EqualTo(outcome));
            Assert.That(actor.RecoveryRemaining, Is.EqualTo(recovery).Within(.00001f));
            Assert.That(actor.CurrentAttackDurationSeconds, Is.EqualTo(.63f + recovery).Within(.00001f));
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
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidTimeCannotCorruptState(float seconds)
        {
            MeleeCombatant actor = new MeleeCombatant();
            Assert.Throws<ArgumentOutOfRangeException>(() => actor.Advance(seconds));
            Assert.That(actor.Stamina, Is.EqualTo(100f));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
        }
    }
}
