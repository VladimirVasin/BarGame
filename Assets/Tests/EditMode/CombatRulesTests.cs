using System;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CombatRulesTests
    {
        private static MeleeCombatSettings S => MeleeCombatSettings.Crowbar;
        private const float Eps = .0001f;

        /// <summary>Raise the guard early enough that the next contact is an ordinary block, not a parry.</summary>
        private static void HoldGuard(MeleeCombatant actor)
        {
            actor.SetBlocking(true);
            actor.Advance(S.ParryWindowSeconds + .01f);
        }

        /// <summary>Take three frontal blocks so the meter has room to regenerate; ends Ready with guard released.</summary>
        private static float SpendThreeBlocks(MeleeCombatant actor)
        {
            HoldGuard(actor);
            for (int i = 0; i < 3; i++)
                Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.Blocked));
            actor.Advance(.3f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            actor.SetBlocking(false);
            return S.MaxStamina - 3f * S.BlockCost;
        }

        [Test]
        public void CommittedMissIsFreeAndCannotCancelIntoAttackOrBlock()
        {
            MeleeCombatant actor = new MeleeCombatant();
            Assert.That(actor.TryStartStep(), Is.True);
            actor.Advance(S.StepDurationSeconds);
            float afterStep = S.MaxStamina - S.StepCost;
            Assert.That(actor.Stamina, Is.EqualTo(afterStep).Within(Eps), "The step's own delay withholds breath.");
            // Let the step-attack grace pass so this is an ordinary full swing.
            actor.Advance(S.StepAttackGraceSeconds + .01f);
            float start = S.StepDurationSeconds + S.StepAttackGraceSeconds + .01f;
            Assert.That(actor.TryStartAttack(), Is.True);
            Assert.That(actor.IsChained, Is.False);
            Assert.That(actor.Stamina, Is.EqualTo(afterStep).Within(Eps), "A swing costs no breath.");
            actor.SetBlocking(true);
            Assert.That(actor.IsBlocking, Is.False);
            Assert.That(actor.TryStartAttack(), Is.False);
            Assert.That(actor.TryRegisterHit(1, actor.AttackSequence), Is.False);
            actor.SetBlocking(false);
            actor.Advance(1f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Recovery));
            // Breath returns during recovery, from the end of the live arc.
            float regenerated = (start + 1f - Math.Max(S.RegenerationDelaySeconds,
                start + S.WindupSeconds + S.ActiveSeconds)) * S.StaminaPerSecond;
            Assert.That(actor.Stamina, Is.EqualTo(afterStep + regenerated).Within(Eps));
            actor.SetBlocking(true);
            Assert.That(actor.IsBlocking, Is.False);
            Assert.That(actor.TryStartAttack(), Is.False);
            actor.Advance(.5f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.IsBlocking, Is.True);
            Assert.That(actor.Stamina, Is.EqualTo(afterStep + regenerated).Within(Eps), "A held guard prevents regeneration.");
            actor.SetBlocking(false);
            actor.Advance(.5f);
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina).Within(Eps));
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
            float spent = SpendThreeBlocks(fine);
            SpendThreeBlocks(hitch);
            fine.TryStartAttack();
            hitch.TryStartAttack();
            for (int i = 0; i < 200; i++) fine.Advance(0.01f);
            hitch.Advance(2f);
            Assert.That(fine.Phase, Is.EqualTo(hitch.Phase));
            Assert.That(fine.Stamina, Is.EqualTo(hitch.Stamina).Within(Eps));
            // Blocks re-armed the delay at .13; the swing began at .43 and its arc ended at 1.06.
            float start = S.ParryWindowSeconds + .01f + .3f;
            float regenFrom = Math.Max(S.ParryWindowSeconds + .01f + S.RegenerationDelaySeconds,
                start + S.WindupSeconds + S.ActiveSeconds);
            Assert.That(hitch.Stamina, Is.EqualTo(spent + (start + 2f - regenFrom) * S.StaminaPerSecond).Within(Eps));
        }

        [Test]
        public void FrontalGuardSpendsBreathThenBreaksAtHalfDamageWhileRearHitBypassesIt()
        {
            MeleeCombatant actor = new MeleeCombatant();
            HoldGuard(actor);
            int blocks = (int)(S.MaxStamina / S.BlockCost);
            for (int i = 0; i < blocks; i++)
                Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.Blocked));
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth));
            Assert.That(actor.Stamina, Is.Zero.Within(Eps));
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.GuardBroken));
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth - S.Damage * S.GuardBreakDamageScale).Within(Eps));
            Assert.That(actor.Stamina, Is.Zero.Within(Eps), "A break keeps whatever breath is left; it never zeroes it.");
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.GuardBroken));
            Assert.That(actor.ActionRemaining, Is.EqualTo(S.GuardBreakSeconds).Within(Eps));
            Assert.That(actor.TryStartAttack(), Is.False);
            actor.Advance(.3f);
            Assert.That(actor.ReceiveHit(5f, S.BlockCost, false), Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.GuardBroken), "Another hit cannot shorten guard break.");
            Assert.That(actor.ActionRemaining, Is.EqualTo(S.StaggerSeconds).Within(Eps), "A fresh stagger extends the stun it lands in.");
            actor.Advance(.3f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.GuardBroken));
            // Breath regenerates through the whole break once the last block's delay has passed.
            float clock = S.ParryWindowSeconds + .01f + .6f;
            float delayEnd = S.ParryWindowSeconds + .01f + S.RegenerationDelaySeconds;
            Assert.That(actor.Stamina, Is.EqualTo((clock - delayEnd) * S.StaminaPerSecond).Within(Eps),
                "Received hits never delay regeneration.");
            actor.Advance(.16f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            actor.Reset();
            HoldGuard(actor);
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, false), Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth - S.Damage));
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina));
        }

        [Test]
        public void AFreshGuardPressParriesALightSwingForFreeUntilTheReArmElapses()
        {
            var actor = new MeleeCombatant();
            actor.SetBlocking(true);
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.Parried));
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth));
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.GuardImpact));
            Assert.That(actor.ActionRemaining, Is.EqualTo(S.ParryImpactSeconds).Within(Eps));
            Assert.That(actor.IsBlocking, Is.True);
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.Blocked),
                "One press parries once; the guard behind it still blocks.");
            actor.Advance(.4f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            actor.SetBlocking(false);
            actor.SetBlocking(true);
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.Blocked),
                "A press before the re-arm is an ordinary block.");
            actor.Advance(.4f);
            actor.SetBlocking(false);
            actor.Advance(S.ParryRearmSeconds);
            actor.SetBlocking(true);
            actor.Advance(S.ParryWindowSeconds - .001f);
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.Parried),
                "A re-armed press parries inside its window.");
            actor.Advance(.4f);
            actor.SetBlocking(false);
            actor.Advance(S.ParryRearmSeconds);
            actor.SetBlocking(true);
            actor.Advance(S.ParryWindowSeconds + .001f);
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.Blocked),
                "The window closes after ParryWindowSeconds.");
            actor.Advance(.4f);
            actor.SetBlocking(false);
            actor.Advance(S.ParryRearmSeconds);
            actor.SetBlocking(true);
            Assert.That(actor.ReceiveHit(S.Damage + S.ChargeDamageBonus, S.BlockCost + S.ChargeBlockCostBonus, true, 1f),
                Is.EqualTo(MeleeHitResult.Blocked), "A heavy swing cannot be parried, only blocked.");
            Assert.That(actor.ActionRemaining, Is.EqualTo(S.GuardImpactSeconds + S.ChargeGuardImpactBonus).Within(Eps));
            actor.Reset();
            actor.ReceiveHit(S.Damage, S.BlockCost, false);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Stagger));
            actor.SetBlocking(true);
            actor.Advance(S.StaggerSeconds);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.Blocked),
                "A guard raised while stunned never parries.");
        }

        [Test]
        public void ParryCostsNothingAndDoesNotDelayRegeneration()
        {
            var actor = new MeleeCombatant();
            float spent = SpendThreeBlocks(actor);
            actor.Advance(S.ParryRearmSeconds);
            float clock = S.ParryWindowSeconds + .01f + .3f + S.ParryRearmSeconds;
            float delayEnd = S.ParryWindowSeconds + .01f + S.RegenerationDelaySeconds;
            float expected = spent + (clock - delayEnd) * S.StaminaPerSecond;
            Assert.That(actor.Stamina, Is.EqualTo(expected).Within(Eps));
            actor.SetBlocking(true);
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.Parried));
            Assert.That(actor.Stamina, Is.EqualTo(expected).Within(Eps));
            actor.SetBlocking(false);
            actor.Advance(.1f);
            Assert.That(actor.Stamina, Is.EqualTo(expected + .1f * S.StaminaPerSecond).Within(Eps),
                "Breath keeps returning straight through the parry recoil.");
        }

        [Test]
        public void ParryStopsTheSwingAtOnceAndOutranksABlockButNotAHit()
        {
            var actor = new MeleeCombatant();
            actor.TryStartAttack();
            actor.Advance(.5f);
            int sequence = actor.AttackSequence;
            Assert.That(actor.TryRegisterHit(1, sequence), Is.True);
            Assert.That(actor.RecordAttackOutcome(MeleeHitResult.Parried, sequence), Is.True);
            Assert.That(actor.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Parried));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Recovery));
            Assert.That(actor.AttackElapsed, Is.EqualTo(actor.AttackActiveEnd).Within(Eps));
            Assert.That(actor.RecoveryRemaining, Is.EqualTo(S.ParriedRecoverySeconds).Within(Eps));
            Assert.That(actor.RecordAttackOutcome(MeleeHitResult.Blocked, sequence), Is.True);
            Assert.That(actor.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Parried), "A block collected in the same step cannot outrank the parry.");
            Assert.That(actor.Advance(.1f).HasActiveWindow, Is.False, "A parried crowbar reaches nobody else.");
            actor.Reset();
            actor.TryStartAttack();
            actor.Advance(.5f);
            sequence = actor.AttackSequence;
            actor.TryRegisterHit(1, sequence);
            actor.TryRegisterHit(2, sequence);
            Assert.That(actor.RecordAttackOutcome(MeleeHitResult.Parried, sequence), Is.True);
            Assert.That(actor.RecordAttackOutcome(MeleeHitResult.Hit, sequence), Is.True);
            Assert.That(actor.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Hit));
            Assert.That(actor.AttackRecoverySeconds, Is.EqualTo(S.HitRecoverySeconds).Within(Eps));
        }

        [Test]
        public void CounterHitPunishesCommitmentAndAWhiffPunishNeverHelpsTheWhiffer()
        {
            float counter = S.StaggerSeconds + S.CounterHitStaggerBonus;
            var actor = new MeleeCombatant();
            actor.TryStartAttack();
            actor.Advance(.2f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Windup));
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, false), Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Stagger));
            Assert.That(actor.ActionRemaining, Is.EqualTo(counter).Within(Eps), "A hit during the windup is a counter-hit.");
            actor.Advance(counter - .01f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Stagger));
            actor.Advance(.02f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));

            actor.Reset();
            actor.RequestCharge();
            actor.Advance(.3f);
            actor.ReceiveHit(S.Damage, S.BlockCost, false);
            Assert.That(actor.IsCharging, Is.False);
            Assert.That(actor.ActionRemaining, Is.EqualTo(counter).Within(Eps), "A hit during a charge is a counter-hit.");

            actor.Reset();
            actor.TryStartAttack();
            actor.Advance(.5f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Active));
            actor.ReceiveHit(S.Damage, S.BlockCost, false);
            Assert.That(actor.ActionRemaining, Is.EqualTo(S.StaggerSeconds).Within(Eps), "A trade in the live arc is an ordinary stagger.");

            actor.Reset();
            actor.TryStartAttack();
            actor.Advance(.5f);
            actor.TryRegisterHit(1, actor.AttackSequence);
            actor.RecordAttackOutcome(MeleeHitResult.Hit, actor.AttackSequence);
            actor.Advance(.2f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Recovery));
            actor.ReceiveHit(S.Damage, S.BlockCost, false);
            Assert.That(actor.ActionRemaining, Is.EqualTo(S.StaggerSeconds).Within(Eps), "Recovery after a landed hit is not exposed.");

            actor.Reset();
            actor.TryStartAttack();
            actor.Advance(.5f);
            actor.CancelAttackOnObstacle();
            actor.ReceiveHit(S.Damage, S.BlockCost, false);
            Assert.That(actor.ActionRemaining, Is.EqualTo(counter).Within(Eps), "A swing stopped by a wall is exposed.");

            for (float t = 0f; t < S.RecoverySeconds; t += .1f)
            {
                actor.Reset();
                actor.TryStartAttack();
                actor.Advance(S.WindupSeconds + S.ActiveSeconds + t + .000001f);
                Assert.That(actor.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Miss));
                float readyWithoutHit = actor.RecoveryRemaining;
                actor.ReceiveHit(S.Damage, S.BlockCost, false);
                Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Stagger));
                Assert.That(actor.ActionRemaining, Is.GreaterThanOrEqualTo(readyWithoutHit - Eps),
                    "Punishing a whiff can never end the whiffer's exposure earlier.");
                Assert.That(actor.ActionRemaining, Is.EqualTo(Math.Max(counter, readyWithoutHit)).Within(Eps));
            }

            actor.Reset();
            actor.ReceiveHit(S.Damage + S.ChargeDamageBonus, S.BlockCost, false, 1f);
            Assert.That(actor.ActionRemaining, Is.EqualTo(S.StaggerSeconds + S.ChargeStaggerBonus).Within(Eps),
                "A heavy blow staggers longer.");
        }

        private static void AdvanceBy(MeleeCombatant actor, float seconds, bool hitch)
        {
            if (hitch) { actor.Advance(seconds); return; }
            for (float t = 0f; t < seconds - Eps; t += .05f) actor.Advance(Math.Min(.05f, seconds - t));
        }

        [TestCase(MeleeHitResult.Hit, MeleeSwing.Backhand)]
        [TestCase(MeleeHitResult.GuardBroken, MeleeSwing.Backhand)]
        [TestCase(MeleeHitResult.Ignored, MeleeSwing.Backhand)]
        [TestCase(MeleeHitResult.Blocked, MeleeSwing.Forehand)]
        [TestCase(MeleeHitResult.Parried, MeleeSwing.Forehand)]
        public void OnlyASwingThatGoesThroughHandsTheNextOneToTheOtherSide(MeleeHitResult result, MeleeSwing next)
        {
            var actor = new MeleeCombatant();
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Forehand));
            foreach (bool hitch in new[] { false, true })
            {
                actor.Reset();
                Assert.That(actor.TryStartAttack(), Is.True);
                Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Forehand), "The first swing is the forehand.");
                AdvanceBy(actor, S.WindupSeconds + .05f, hitch);
                if (result != MeleeHitResult.Ignored)
                {
                    Assert.That(actor.TryRegisterHit(1, actor.AttackSequence), Is.True);
                    Assert.That(actor.RecordAttackOutcome(result, actor.AttackSequence), Is.True);
                }
                AdvanceBy(actor, 2f, hitch);
                Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
                Assert.That(actor.TryStartAttack(), Is.True);
                Assert.That(actor.Swing, Is.EqualTo(next), hitch ? "one hitch" : "fine steps");
            }
        }

        [Test]
        public void SideCuesOutrankTheRhythmAndAChargeKeepsItsSide()
        {
            var actor = new MeleeCombatant();
            // A wall stops the swing: the same side comes again.
            actor.TryStartAttack();
            actor.Advance(S.WindupSeconds + .05f);
            Assert.That(actor.CancelAttackOnObstacle(), Is.True);
            actor.Advance(2f);
            actor.TryStartAttack();
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Forehand), "An obstacle repeats the side.");
            actor.Advance(2f);
            // The return swing out of the buffer after a landed hit comes from the other side.
            actor.TryStartAttack();
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Backhand), "A miss hands the turn over.");
            actor.Advance(S.WindupSeconds + .05f);
            actor.TryRegisterHit(1, actor.AttackSequence);
            actor.RecordAttackOutcome(MeleeHitResult.Hit, actor.AttackSequence);
            actor.Advance(.2f);
            Assert.That(actor.RequestAttack(), Is.True);
            actor.Advance(.2f);
            Assert.That(actor.IsChained, Is.True);
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Forehand), "The return swing comes from the other side.");
            actor.Advance(2f);
            // Being hit in the windup leaves the turn where it was.
            actor.TryStartAttack();
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Backhand));
            actor.ReceiveHit(S.Damage, S.BlockCost, false);
            actor.Advance(2f);
            actor.TryStartAttack();
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Backhand), "An interrupted swing keeps its turn.");
            actor.Advance(2f);

            // A target off the facing line outranks the rhythm, read when the swing commits.
            actor.ObserveLateralCue(1);
            Assert.That(actor.RequestCharge(), Is.True);
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Backhand), "A target on the right calls the backhand.");
            actor.ObserveLateralCue(-1);
            actor.Advance(.3f);
            Assert.That(actor.ReleaseCharge(), Is.True);
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Backhand), "Release keeps the side the held pose showed.");
            actor.Advance(2f);
            actor.ObserveLateralCue(0);
            actor.TryStartAttack();
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Forehand), "Squared up, the rhythm decides.");
            actor.Advance(2f);
            actor.ObserveLateralCue(-1);
            actor.TryStartAttack();
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Forehand), "A target on the left calls the forehand over the rhythm.");
            actor.Advance(2f);
            actor.ObserveLateralCue(0);
            Assert.That(actor.RequestCharge(), Is.True);
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Backhand));
            Assert.That(actor.CancelCharge(), Is.True);
            actor.Advance(.5f);
            actor.TryStartAttack();
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Backhand), "A cancelled charge spends no turn.");
            actor.Advance(2f);

            // A side step just taken sets the step attack's side, and only within the grace.
            Assert.That(actor.TryStartStep(1), Is.True);
            actor.Advance(S.StepDurationSeconds);
            Assert.That(actor.TryStartAttack(), Is.True);
            Assert.That(actor.IsChained, Is.True);
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Backhand), "A step to the right swings with the body.");
            actor.Advance(2f);
            Assert.That(actor.TryStartStep(1), Is.True);
            actor.Advance(S.StepDurationSeconds + S.StepAttackGraceSeconds + .01f);
            actor.TryStartAttack();
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Forehand), "Past the grace the rhythm decides.");
            actor.Advance(2f);
            // A queued step keeps its direction for the step attack that follows it.
            actor.TryStartAttack();
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Backhand));
            actor.Advance(S.AttackDurationSeconds - .1f);
            Assert.That(actor.RequestStep(1), Is.True);
            // The queued step fires at the tail's boundary; a hair past its own end keeps
            // the read inside the step-attack grace rather than on the rounding edge.
            actor.Advance(.1f + S.StepDurationSeconds + .01f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.TryStartAttack(), Is.True);
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Backhand), "A buffered step to the right swings the backhand over the rhythm.");

            actor.Reset();
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Forehand));
        }

        [Test]
        public void LandedHitKeepsInitiativeAndArmsOneBackhandOutOfTheBuffer()
        {
            var actor = new MeleeCombatant();
            actor.TryStartAttack();
            actor.Advance(.5f);
            actor.TryRegisterHit(1, actor.AttackSequence);
            Assert.That(actor.RecordAttackOutcome(MeleeHitResult.Hit, actor.AttackSequence), Is.True);
            Assert.That(actor.AttackRecoverySeconds, Is.EqualTo(S.HitRecoverySeconds).Within(Eps));
            actor.Advance(.2f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Recovery));
            Assert.That(actor.RequestAttack(), Is.True);
            int first = actor.AttackSequence;
            actor.Advance(.2f);
            Assert.That(actor.AttackSequence, Is.EqualTo(first + 1));
            Assert.That(actor.IsChained, Is.True, "A landed hit arms the backhand for a queued press.");
            Assert.That(actor.AttackWindupSeconds, Is.EqualTo(S.ChainWindupSeconds).Within(Eps));
            Assert.That(actor.AttackElapsed, Is.EqualTo(.05f).Within(Eps));
            actor.Advance(.2f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Active));
            actor.TryRegisterHit(1, actor.AttackSequence);
            actor.RecordAttackOutcome(MeleeHitResult.Hit, actor.AttackSequence);
            actor.Advance(.3f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Recovery));
            Assert.That(actor.RequestAttack(), Is.True);
            actor.Advance(.2f);
            Assert.That(actor.IsChained, Is.False, "The backhand never chains into itself.");
            Assert.That(actor.AttackWindupSeconds, Is.EqualTo(S.WindupSeconds).Within(Eps));

            actor.Reset();
            actor.TryStartAttack();
            actor.Advance(.5f);
            actor.TryRegisterHit(1, actor.AttackSequence);
            actor.RecordAttackOutcome(MeleeHitResult.Hit, actor.AttackSequence);
            actor.Advance(1f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.TryStartAttack(), Is.True);
            Assert.That(actor.IsChained, Is.False, "Only a queued press takes the backhand.");

            actor.Reset();
            actor.TryStartAttack();
            actor.Advance(.5f);
            actor.TryRegisterHit(1, actor.AttackSequence);
            actor.RecordAttackOutcome(MeleeHitResult.Hit, actor.AttackSequence);
            actor.ReceiveHit(S.Damage, S.BlockCost, false);
            actor.Advance(S.StaggerSeconds - .1f);
            Assert.That(actor.RequestAttack(), Is.True);
            actor.Advance(.2f);
            Assert.That(actor.IsAttacking, Is.True);
            Assert.That(actor.IsChained, Is.False, "Being hit disarms the backhand.");
        }

        [Test]
        public void AStepEndsIntoAStepAttackWithinItsGrace()
        {
            var actor = new MeleeCombatant();
            actor.TryStartStep();
            actor.Advance(S.StepDurationSeconds);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.TryStartAttack(), Is.True);
            Assert.That(actor.IsChained, Is.True);
            Assert.That(actor.AttackWindupSeconds, Is.EqualTo(S.ChainWindupSeconds).Within(Eps));

            actor.Reset();
            actor.TryStartStep();
            actor.Advance(S.StepDurationSeconds);
            actor.Advance(S.StepAttackGraceSeconds + .01f);
            Assert.That(actor.TryStartAttack(), Is.True);
            Assert.That(actor.IsChained, Is.False, "The grace after a step is short.");

            actor.Reset();
            actor.TryStartStep();
            actor.Advance(S.StepDurationSeconds - .1f);
            Assert.That(actor.RequestAttack(), Is.True, "The step's settle accepts a queued strike.");
            actor.Advance(.2f);
            Assert.That(actor.IsChained, Is.True);
            Assert.That(actor.AttackElapsed, Is.EqualTo(.1f).Within(Eps));
        }

        [TestCase(MeleePhase.Recovery, MeleeBufferedAction.Attack)]
        [TestCase(MeleePhase.Recovery, MeleeBufferedAction.Charge)]
        [TestCase(MeleePhase.Recovery, MeleeBufferedAction.Step)]
        [TestCase(MeleePhase.Stagger, MeleeBufferedAction.Attack)]
        [TestCase(MeleePhase.Stagger, MeleeBufferedAction.Charge)]
        [TestCase(MeleePhase.Stagger, MeleeBufferedAction.Step)]
        [TestCase(MeleePhase.GuardImpact, MeleeBufferedAction.Attack)]
        [TestCase(MeleePhase.GuardImpact, MeleeBufferedAction.Charge)]
        [TestCase(MeleePhase.GuardImpact, MeleeBufferedAction.Step)]
        [TestCase(MeleePhase.GuardBroken, MeleeBufferedAction.Attack)]
        [TestCase(MeleePhase.GuardBroken, MeleeBufferedAction.Charge)]
        [TestCase(MeleePhase.GuardBroken, MeleeBufferedAction.Step)]
        [TestCase(MeleePhase.Step, MeleeBufferedAction.Attack)]
        [TestCase(MeleePhase.Step, MeleeBufferedAction.Charge)]
        [TestCase(MeleePhase.Step, MeleeBufferedAction.Step)]
        public void BufferedInputFromStunTailsFiresOnce(MeleePhase phase, MeleeBufferedAction action)
        {
            var actor = new MeleeCombatant();
            switch (phase)
            {
                case MeleePhase.Recovery:
                    actor.TryStartAttack();
                    actor.Advance(actor.Settings.AttackDurationSeconds - .3f);
                    Assert.That(Request(actor, action), Is.False, "A press outside the final window is discarded.");
                    actor.Advance(.2f);
                    break;
                case MeleePhase.Stagger:
                    actor.ReceiveHit(S.Damage, S.BlockCost, false);
                    actor.Advance(S.StaggerSeconds - .1f);
                    break;
                case MeleePhase.GuardImpact:
                    HoldGuard(actor);
                    actor.ReceiveHit(S.Damage, S.BlockCost, true);
                    actor.SetBlocking(false);
                    actor.Advance(S.GuardImpactSeconds - .1f);
                    break;
                case MeleePhase.GuardBroken:
                    HoldGuard(actor);
                    actor.ReceiveHit(S.Damage, S.MaxStamina + 1f, true);
                    actor.SetBlocking(false);
                    actor.Advance(S.GuardBreakSeconds - .1f);
                    break;
                case MeleePhase.Step:
                    actor.TryStartStep();
                    actor.Advance(S.StepDurationSeconds - .1f);
                    break;
            }
            Assert.That(actor.Phase, Is.EqualTo(phase));
            Assert.That(actor.ActionRemaining, Is.EqualTo(.1f).Within(Eps));
            float stamina = actor.Stamina;
            int sequence = actor.AttackSequence;
            Assert.That(Request(actor, action), Is.True);
            Assert.That(Request(actor, action), Is.True, "Repeated presses share one pending slot.");
            Assert.That(actor.BufferedAction, Is.EqualTo(action));
            Assert.That(actor.Stamina, Is.EqualTo(stamina).Within(Eps), "Queueing does not spend before the action starts.");
            actor.Advance(.15f);
            Assert.That(actor.BufferedAction, Is.EqualTo(MeleeBufferedAction.None));
            Assert.That(actor.AttackSequence, Is.EqualTo(sequence + 1));
            switch (action)
            {
                case MeleeBufferedAction.Attack:
                    Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Windup));
                    Assert.That(actor.AttackElapsed, Is.EqualTo(.05f).Within(Eps));
                    break;
                case MeleeBufferedAction.Charge:
                    Assert.That(actor.IsCharging, Is.True);
                    Assert.That(actor.Charge01, Is.EqualTo(.05f / S.ChargeSeconds).Within(Eps));
                    break;
                case MeleeBufferedAction.Step:
                    Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Step));
                    Assert.That(actor.StepElapsed, Is.EqualTo(.05f).Within(Eps));
                    Assert.That(actor.Stamina, Is.EqualTo(stamina - S.StepCost).Within(Eps));
                    break;
            }
            actor.Advance(3f);
            Assert.That(actor.AttackSequence, Is.EqualTo(sequence + 1), "One press never repeats itself.");
            if (action == MeleeBufferedAction.Charge)
            {
                Assert.That(actor.IsCharging, Is.True, "A queued charge waits for its release.");
                Assert.That(actor.ReleaseCharge(), Is.True);
            }
            else Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
        }

        private static bool Request(MeleeCombatant actor, MeleeBufferedAction action) => action switch
        {
            MeleeBufferedAction.Attack => actor.RequestAttack(),
            MeleeBufferedAction.Charge => actor.RequestCharge(),
            _ => actor.RequestStep()
        };

        [Test]
        public void AttackPressBuffersOnlyNearRecoveryEndAndIsConsumedOnce()
        {
            MeleeCombatant actor = new MeleeCombatant();
            Assert.That(actor.RequestAttack(), Is.True, "An idle press still starts immediately.");
            int firstSwing = actor.AttackSequence;
            Assert.That(actor.RequestAttack(), Is.False, "Windup cannot queue a combo.");
            actor.Advance(actor.Settings.AttackDurationSeconds - .3f);
            Assert.That(actor.RequestAttack(), Is.False, "A press outside the final window is discarded.");
            actor.Advance(.21f);
            actor.SetBlocking(true);
            Assert.That(actor.RequestAttack(), Is.True);
            Assert.That(actor.HasBufferedAttack, Is.True);
            actor.Advance(.1f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Windup));
            Assert.That(actor.IsBlocking, Is.False, "An explicitly queued attack takes priority over held guard.");
            Assert.That(actor.AttackElapsed, Is.EqualTo(.01f).Within(.00001f));
            Assert.That(actor.AttackSequence, Is.EqualTo(firstSwing + 1));
            Assert.That(actor.HasBufferedAttack, Is.False);
            actor.Advance(2f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.AttackSequence, Is.EqualTo(firstSwing + 1), "One press never repeats itself.");

            // Breath returns through recovery, so the tax must exceed what one swing regenerates.
            var exhausted = new MeleeCombatant(new MeleeCombatSettings(attackCost: 90f));
            exhausted.TryStartAttack();
            exhausted.Advance(exhausted.Settings.AttackDurationSeconds - .08f);
            Assert.That(exhausted.Stamina, Is.LessThan(90f));
            Assert.That(exhausted.RequestAttack(), Is.False);
            Assert.That(exhausted.HasBufferedAttack, Is.False,
                "An unaffordable press must not become an attack after a later regeneration.");
            var winded = new MeleeCombatant(new MeleeCombatSettings(stepCost: 80f));
            winded.TryStartStep();
            winded.Advance(S.StepDurationSeconds - .1f);
            Assert.That(winded.RequestStep(), Is.False, "An unaffordable step cannot be queued either.");
        }

        [Test]
        public void BufferedAttackStartsAtRecoveryBoundaryEvenWhenHitchCrossesItsActivePhase()
        {
            MeleeCombatant fine = new MeleeCombatant();
            MeleeCombatant hitch = new MeleeCombatant();
            foreach (MeleeCombatant actor in new[] { fine, hitch })
            {
                SpendThreeBlocks(actor);
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
            Assert.That(fine.Stamina, Is.EqualTo(hitch.Stamina).Within(Eps));
            Assert.That(hitch.Stamina, Is.EqualTo(S.MaxStamina).Within(Eps));
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
                case "hit": actor.ReceiveHit(10f, S.BlockCost, false); break;
                case "defeat": actor.ReceiveHit(S.MaxHealth, S.BlockCost, false); break;
                case "reset": actor.Reset(); break;
                case "cancel": actor.CancelAction(); break;
                case "guard": actor.SetBlocking(true); break;
                case "step": Assert.That(actor.RequestStep(), Is.True, "A step press takes over the single slot."); break;
                case "chargeCancel": actor.CancelCharge(); break;
            }
            Assert.That(actor.HasBufferedAttack, Is.False);
            Assert.That(actor.ReleaseCharge(), Is.False);
            int sequence = actor.AttackSequence;
            Assert.That(actor.Advance(2f).HasActiveWindow, Is.False);
            Assert.That(actor.IsAttacking, Is.False);
            if (interruption == "step")
            {
                Assert.That(actor.AttackSequence, Is.EqualTo(sequence + 1), "The queued step fired once.");
                Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina).Within(Eps), "and its breath has already returned.");
            }
            else Assert.That(actor.AttackSequence, Is.EqualTo(sequence));
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
            Assert.That(actor.RecoveryRemaining, Is.EqualTo(S.ObstacleRecoverySeconds - .2f).Within(.00001f));
            actor.Advance(S.ObstacleRecoverySeconds - .2f + .01f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.CancelAttackOnObstacle(), Is.False);
        }

        [TestCase(MeleeAttackOutcome.Hit, 0f)]
        [TestCase(MeleeAttackOutcome.Blocked, 0f)]
        [TestCase(MeleeAttackOutcome.Miss, 0f)]
        [TestCase(MeleeAttackOutcome.Obstacle, 0f)]
        [TestCase(MeleeAttackOutcome.Parried, 0f)]
        [TestCase(MeleeAttackOutcome.Hit, 1f)]
        [TestCase(MeleeAttackOutcome.Blocked, 1f)]
        [TestCase(MeleeAttackOutcome.Miss, 1f)]
        [TestCase(MeleeAttackOutcome.Obstacle, 1f)]
        public void ContactOutcomeOwnsRecoveryButKeepsTheAuthoredAnimationEndpoint(MeleeAttackOutcome outcome, float power)
        {
            float recovery = (outcome switch
            {
                MeleeAttackOutcome.Hit => S.HitRecoverySeconds,
                MeleeAttackOutcome.Blocked => S.BlockRecoverySeconds,
                MeleeAttackOutcome.Obstacle => S.ObstacleRecoverySeconds,
                MeleeAttackOutcome.Parried => S.ParriedRecoverySeconds,
                _ => S.RecoverySeconds
            }) * (1f + S.ChargeRecoveryBonus * power);
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
            else if (outcome == MeleeAttackOutcome.Parried)
            {
                Assert.That(actor.TryRegisterHit(1, actor.AttackSequence), Is.True);
                Assert.That(actor.RecordAttackOutcome(MeleeHitResult.Parried, actor.AttackSequence), Is.True);
            }
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
            Assert.That(actor.AttackRecoverySeconds, Is.EqualTo(S.HitRecoverySeconds).Within(Eps));
            Assert.That(actor.RecordAttackOutcome(result, sequence - 1), Is.False);

            foreach (float damage in new[] { S.Damage, S.MaxHealth })
            {
                actor.Reset();
                actor.TryStartAttack();
                actor.Advance(.5f);
                sequence = actor.AttackSequence;
                actor.TryRegisterHit(1, sequence);
                actor.ReceiveHit(damage, S.BlockCost, false);
                MeleePhase interrupted = actor.Phase;
                Assert.That(actor.RecordAttackOutcome(result, sequence), Is.True,
                    "Collect-before-apply preserves the source's contact even if another contact interrupts it first.");
                Assert.That(actor.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Hit));
                Assert.That(actor.Phase, Is.EqualTo(interrupted), "Recording an outgoing result never revives the interrupted action.");
                actor.Advance(.01f);
                Assert.That(actor.RecordAttackOutcome(result, sequence), Is.False, "A collected window expires at the next simulation step.");
            }
        }

        [TestCase(MeleeHitResult.Hit)]
        [TestCase(MeleeHitResult.Blocked)]
        public void LateHitchOutcomePreservesTheSameReadyBoundaryAndRegeneration(MeleeHitResult result)
        {
            var fine = new MeleeCombatant();
            var hitch = new MeleeCombatant();
            float spent = SpendThreeBlocks(fine);
            SpendThreeBlocks(hitch);
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
            Assert.That(hitch.Stamina, Is.EqualTo(fine.Stamina).Within(Eps));
            float start = S.ParryWindowSeconds + .01f + .3f;
            float regenFrom = Math.Max(S.ParryWindowSeconds + .01f + S.RegenerationDelaySeconds,
                start + S.WindupSeconds + S.ActiveSeconds);
            Assert.That(hitch.Stamina, Is.EqualTo(spent + (start + 2f - regenFrom) * S.StaminaPerSecond).Within(Eps),
                "Recovery length no longer decides when breath returns; the end of the arc does.");
        }

        [Test]
        public void UnblockedHitInterruptsAttackButNeverDelaysRegeneration()
        {
            MeleeCombatant actor = new MeleeCombatant();
            float spent = SpendThreeBlocks(actor);
            actor.TryStartAttack();
            MeleeAdvanceResult step = actor.Advance(0.5f);
            Assert.That(step.HasActiveWindow, Is.True);
            actor.ReceiveHit(S.Damage, S.BlockCost, false);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Stagger));
            Assert.That(actor.TryRegisterHit(1, step.AttackSequence), Is.False);
            actor.Advance(0.5f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            // The interrupted arc never ended; breath ran through the whole stagger.
            float hitAt = S.ParryWindowSeconds + .01f + .3f + .5f;
            float delayEnd = S.ParryWindowSeconds + .01f + S.RegenerationDelaySeconds;
            float expected = spent + (hitAt + .5f - Math.Max(hitAt, delayEnd)) * S.StaminaPerSecond;
            Assert.That(actor.Stamina, Is.EqualTo(expected).Within(Eps));
            actor.Advance(0.75f);
            Assert.That(actor.Stamina, Is.EqualTo(Math.Min(S.MaxStamina, expected + .75f * S.StaminaPerSecond)).Within(Eps));
        }

        [Test]
        public void ExhaustionRejectsGuardAndStepButNotAttackAndResetRestoresRoundWithoutStaleHits()
        {
            MeleeCombatant actor = new MeleeCombatant();
            HoldGuard(actor);
            int blocks = (int)(S.MaxStamina / S.BlockCost);
            for (int i = 0; i < blocks; i++) actor.ReceiveHit(S.Damage, S.BlockCost, true);
            Assert.That(actor.Stamina, Is.Zero.Within(Eps));
            actor.Advance(.2f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.TryStartStep(), Is.False, "No breath, no step.");
            Assert.That(actor.TryStartAttack(), Is.True, "No breath still swings.");
            actor.Advance(actor.Settings.AttackDurationSeconds + .01f);
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.GuardBroken),
                "A guard held on empty breaks instead of blocking.");
            actor.Advance(S.GuardBreakSeconds + .01f);
            actor.SetBlocking(false);
            Assert.That(actor.RequestCharge(), Is.True);
            Assert.That(actor.ChargeLimit01, Is.Zero.Within(Eps));
            actor.Advance(1f);
            Assert.That(actor.Charge01, Is.Zero.Within(Eps));
            Assert.That(actor.ReleaseCharge(), Is.True);
            Assert.That(actor.AttackPower, Is.Zero, "A hold without breath is an ordinary swing.");
            int obsolete = actor.AttackSequence;
            actor.ReceiveHit(S.MaxHealth, S.BlockCost, false);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Defeated));
            Assert.That(actor.Health, Is.Zero);
            Assert.That(actor.TryStartAttack(), Is.False);
            Assert.That(actor.TryStartStep(), Is.False);
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, false), Is.EqualTo(MeleeHitResult.Ignored));
            float stamina = actor.Stamina;
            actor.Advance(20f);
            Assert.That(actor.Stamina, Is.EqualTo(stamina).Within(Eps), "The defeated do not breathe back.");
            actor.Reset();
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth));
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina));
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
            actor.RequestCharge();
            actor.Advance(.45f);
            actor.ReleaseCharge();
            MeleeAdvanceResult step = actor.Advance(0.1f);
            float spent = S.MaxStamina - S.ChargeStaminaCost * .5f;
            actor.SetBlocking(true);
            actor.CancelAction();
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth));
            Assert.That(actor.Stamina, Is.EqualTo(spent).Within(Eps));
            Assert.That(actor.IsBlocking, Is.False);
            Assert.That(actor.TryRegisterHit(1, step.AttackSequence), Is.False);
            Assert.That(actor.Advance(0.1f).HasActiveWindow, Is.False);
            actor.ReceiveHit(S.MaxHealth, S.BlockCost, false);
            actor.CancelAction();
            Assert.That(actor.IsDefeated, Is.True, "Cancellation cannot resurrect a finished round.");
        }

        [Test]
        public void SuccessfulGuardCommitsBrieflyButStillProtectsAgainstFrontalFollowups()
        {
            var actor = new MeleeCombatant();
            HoldGuard(actor);
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.Blocked));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.GuardImpact));
            Assert.That(actor.IsBlocking, Is.True);
            Assert.That(actor.TryStartAttack(), Is.False);
            Assert.That(actor.TryStartStep(), Is.False);
            actor.Advance(.1f);
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.Blocked));
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth));
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina - 2f * S.BlockCost).Within(Eps));
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
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina - S.StepCost));
            actor.SetBlocking(true);
            Assert.That(actor.IsBlocking, Is.False);
            Assert.That(actor.TryStartAttack(), Is.False);
            Assert.That(actor.RequestAttack(), Is.False, "The travel itself accepts no queued strike.");
            Assert.That(actor.TryStartStep(), Is.False);
            Assert.That(actor.HasBufferedAttack, Is.False);
            actor.Advance(S.StepTravelSeconds * .5f);
            Assert.That(actor.StepTravelProgress, Is.EqualTo(.5f).Within(.00001f));
            actor.Advance(S.StepTravelSeconds * .5f);
            Assert.That(actor.StepTravelProgress, Is.EqualTo(1f));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Step), "Finishing travel does not skip the planted recovery.");
            actor.Advance(S.StepRecoverySeconds);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.StepElapsed, Is.EqualTo(S.StepDurationSeconds).Within(.00001f));
            Assert.That(actor.StepTravelProgress, Is.EqualTo(1f), "Runtime must still integrate the last crossed travel interval.");
            Assert.That(actor.StepProgress, Is.EqualTo(1f));
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina - S.StepCost), "A held guard withholds breath through the step.");
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
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, fromFront), Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth - S.Damage));
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina - S.StepCost));
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
            for (int i = 0; i < 90; i++) fine.Advance(.01f);
            hitch.Advance(.9f);
            Assert.That(hitch.Stamina, Is.EqualTo(S.MaxStamina - S.StepCost +
                (.9f - S.RegenerationDelaySeconds) * S.StaminaPerSecond).Within(Eps));
            Assert.That(fine.Stamina, Is.EqualTo(hitch.Stamina).Within(Eps));
            Assert.That(hitch.StepElapsed, Is.EqualTo(fine.StepElapsed));
            Assert.That(hitch.Phase, Is.EqualTo(MeleePhase.Ready));
            var exhausted = new MeleeCombatant(new MeleeCombatSettings(stepCost: 60f));
            exhausted.TryStartStep();
            exhausted.Advance(.5f);
            Assert.That(exhausted.TryStartStep(), Is.False);
            exhausted.Reset();
            Assert.That(exhausted.StepElapsed, Is.Zero);
            Assert.That(exhausted.Stamina, Is.EqualTo(S.MaxStamina));
            Assert.That(exhausted.TryStartStep(), Is.True);
            exhausted.CancelAction();
            Assert.That(exhausted.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(exhausted.StepElapsed, Is.Zero);
            Assert.That(exhausted.Stamina, Is.EqualTo(40f), "Cancellation does not refund defensive movement.");
        }

        [Test]
        public void StepChecksAffordabilityBeforeGivingUpAHeldCharge()
        {
            var actor = new MeleeCombatant();
            actor.RequestCharge();
            actor.Advance(S.ChargeSeconds);
            actor.Advance((S.MaxStamina - S.ChargeStaminaCost) / S.OverholdDrainPerSecond + .01f);
            Assert.That(actor.Stamina, Is.Zero.Within(Eps));
            Assert.That(actor.TryStartStep(), Is.False, "An unaffordable step must not destroy the charge.");
            Assert.That(actor.IsCharging, Is.True);
            Assert.That(actor.Charge01, Is.EqualTo(1f).Within(Eps));
            actor.Reset();
            actor.RequestCharge();
            actor.Advance(.45f);
            Assert.That(actor.TryStartStep(), Is.True);
            Assert.That(actor.IsCharging, Is.False);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Step));
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina - S.ChargeStaminaCost * .5f - S.StepCost).Within(Eps));
        }

        [TestCase(0f, 0f)]
        [TestCase(.45f, .5f)]
        [TestCase(.9f, 1f)]
        [TestCase(3f, 1f)]
        public void ChargeReleaseLatchesAffordablePowerAndTraversesTheAuthoredArc(float heldSeconds, float power)
        {
            float damage = S.Damage + S.ChargeDamageBonus * power;
            float blockCost = S.BlockCost + S.ChargeBlockCostBonus * power;
            float windup = S.WindupSeconds * (1f - power) + S.ChargedWindupSeconds * power;
            float stamina = S.MaxStamina - S.ChargeStaminaCost * power -
                Math.Max(0f, heldSeconds - S.ChargeSeconds) * S.OverholdDrainPerSecond;
            var actor = new MeleeCombatant();
            Assert.That(actor.RequestCharge(), Is.True);
            int sequence = actor.AttackSequence;
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina), "Winding up costs nothing until the hold begins.");
            Assert.That(actor.ChargeLimit01, Is.EqualTo(1f));
            Assert.That(actor.Advance(heldSeconds).HasActiveWindow, Is.False);
            Assert.That(actor.IsCharging, Is.True, "Full charge waits for release, even through a hitch.");
            Assert.That(actor.IsAttacking, Is.False);
            Assert.That(actor.Charge01, Is.EqualTo(power).Within(.00001f));
            Assert.That(actor.Stamina, Is.EqualTo(stamina).Within(Eps));
            Assert.That(actor.TryRegisterHit(1, sequence), Is.False, "A held weapon cannot damage a target.");
            Assert.That(actor.RequestCharge(), Is.True);
            Assert.That(actor.Stamina, Is.EqualTo(stamina).Within(Eps), "Repeated holds cannot repay the base cost.");
            Assert.That(actor.ReleaseCharge(), Is.True);
            Assert.That(actor.AttackSequence, Is.EqualTo(sequence));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Windup));
            Assert.That(actor.Charge01, Is.Zero);
            Assert.That(actor.AttackPower, Is.EqualTo(power).Within(.00001f));
            Assert.That(actor.AttackDamage, Is.EqualTo(damage).Within(Eps));
            Assert.That(actor.AttackBlockCost, Is.EqualTo(blockCost).Within(Eps));
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
            Assert.That(actor.AttackDamage, Is.EqualTo(S.Damage));
            Assert.That(actor.AttackWindupSeconds, Is.EqualTo(S.WindupSeconds));
        }

        [TestCase(10f, .5f)]
        [TestCase(20f, 1f)]
        [TestCase(30f, 1f)]
        public void LimitedBreathCapsChargeAndAHeldCapDrainsWithoutAutomaticRelease(float available, float limit)
        {
            var actor = new MeleeCombatant(new MeleeCombatSettings(maxStamina: available,
                stepCost: Math.Min(S.StepCost, available)));
            Assert.That(actor.RequestCharge(), Is.True);
            Assert.That(actor.ChargeLimit01, Is.EqualTo(limit).Within(Eps));
            Assert.That(actor.Advance(10f).HasActiveWindow, Is.False);
            Assert.That(actor.Charge01, Is.EqualTo(limit).Within(Eps));
            Assert.That(actor.Stamina, Is.Zero.Within(Eps), "A held cap burns breath down to nothing.");
            Assert.That(actor.IsCharging, Is.True);
            actor.Advance(10f);
            Assert.That(actor.Stamina, Is.Zero.Within(Eps), "A held cap must neither drain below zero nor regenerate.");
            Assert.That(actor.IsCharging, Is.True, "It never fires by itself.");
            Assert.That(actor.ReleaseCharge(), Is.True);
            Assert.That(actor.AttackDamage, Is.EqualTo(S.Damage + S.ChargeDamageBonus * limit).Within(Eps));
        }

        [Test]
        public void OverholdDrainsAtASteadyRateOnlyAfterTheCap()
        {
            var actor = new MeleeCombatant();
            actor.RequestCharge();
            actor.Advance(S.ChargeSeconds);
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina - S.ChargeStaminaCost).Within(Eps));
            actor.Advance(1f);
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina - S.ChargeStaminaCost - S.OverholdDrainPerSecond).Within(Eps));
            actor.Advance(10f);
            Assert.That(actor.Stamina, Is.Zero.Within(Eps));
            Assert.That(actor.IsCharging, Is.True);
            Assert.That(actor.ReleaseCharge(), Is.True);
            Assert.That(actor.AttackPower, Is.EqualTo(1f).Within(Eps));
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
            Assert.That(hitch.Stamina, Is.EqualTo(fine.Stamina).Within(Eps));
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
            Assert.That(hitch.Stamina, Is.EqualTo(fine.Stamina).Within(Eps));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BufferedChargeBeginsAtReadyAndAReleasedQueueBecomesOneTap(bool releasedBeforeReady)
        {
            var actor = new MeleeCombatant();
            actor.TryStartAttack();
            int previous = actor.AttackSequence;
            actor.Advance(actor.CurrentAttackDurationSeconds - .3f);
            Assert.That(actor.RequestCharge(), Is.False);
            actor.Advance(.11f);
            Assert.That(actor.RequestCharge(), Is.True);
            if (releasedBeforeReady) Assert.That(actor.ReleaseCharge(), Is.True);
            Assert.That(actor.HasBufferedCharge, Is.True);
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina));
            actor.Advance(.05f);
            Assert.That(actor.IsCharging, Is.False);
            Assert.That(actor.Charge01, Is.Zero, "Recovery time cannot contribute to the next charge.");
            actor.Advance(.15f);
            Assert.That(actor.AttackSequence, Is.EqualTo(previous + 1));
            Assert.That(actor.HasBufferedAttack, Is.False);
            if (releasedBeforeReady)
            {
                Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Windup));
                Assert.That(actor.AttackElapsed, Is.EqualTo(.01f).Within(.00001f));
                Assert.That(actor.AttackPower, Is.Zero);
                Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina));
                Assert.That(actor.ReleaseCharge(), Is.False);
            }
            else
            {
                Assert.That(actor.IsCharging, Is.True);
                Assert.That(actor.Charge01, Is.EqualTo(.01f / S.ChargeSeconds).Within(.00001f));
                Assert.That(actor.ReleaseCharge(), Is.True);
            }
            actor.Advance(3f);
            Assert.That(actor.AttackSequence, Is.EqualTo(previous + 1), "One buffered press must not repeat itself.");
            Assert.That(actor.IsCharging || actor.IsAttacking, Is.False);
        }

        [TestCase("cancel", 0f)]
        [TestCase("guard", 0f)]
        [TestCase("step", 1f)]
        [TestCase("hit", 0f)]
        [TestCase("defeat", 0f)]
        [TestCase("owner", 0f)]
        [TestCase("reset", -1f)]
        public void ChargingCancellationKeepsSpentEffortAndCannotReplayOnRelease(string interruption, float stepsTaken)
        {
            float stamina = stepsTaken < 0f ? S.MaxStamina : S.MaxStamina - S.ChargeStaminaCost * .5f - S.StepCost * stepsTaken;
            var actor = new MeleeCombatant();
            actor.RequestCharge();
            actor.Advance(.45f);
            switch (interruption)
            {
                case "cancel": Assert.That(actor.CancelCharge(), Is.True); break;
                case "guard": actor.SetBlocking(true); Assert.That(actor.IsBlocking, Is.True); break;
                case "step": Assert.That(actor.TryStartStep(), Is.True); break;
                case "hit": actor.ReceiveHit(10f, S.BlockCost, false); break;
                case "defeat": actor.ReceiveHit(S.MaxHealth, S.BlockCost, false); break;
                case "owner": actor.CancelAction(); break;
                case "reset": actor.Reset(); break;
            }
            Assert.That(actor.Stamina, Is.EqualTo(stamina).Within(Eps));
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
            actor.Advance(S.ChargeSeconds);
            actor.ReleaseCharge();
            actor.Advance(S.ChargedWindupSeconds + .02f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Active));
            int sequence = actor.AttackSequence;
            Assert.That(actor.TryRegisterHit(3, sequence), Is.True);
            actor.ReceiveHit(damage, S.BlockCost, false);
            MeleePhase interrupted = actor.Phase;
            Assert.That(actor.AttackPower, Is.EqualTo(1f));
            Assert.That(actor.AttackDamage, Is.EqualTo(S.Damage + S.ChargeDamageBonus));
            Assert.That(actor.AttackBlockCost, Is.EqualTo(S.BlockCost + S.ChargeBlockCostBonus));
            Assert.That(actor.RecordAttackOutcome(MeleeHitResult.Hit, sequence), Is.True);
            Assert.That(actor.Phase, Is.EqualTo(interrupted));
            actor.Reset();
            Assert.That(actor.AttackPower, Is.Zero);
            Assert.That(actor.AttackDamage, Is.EqualTo(S.Damage));
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
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(parryWindowSeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(parryRearmSeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(parryImpactSeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(parriedRecoverySeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(parryMaxPower: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(counterHitStaggerBonus: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(chargeStaggerBonus: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(chargeGuardImpactBonus: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(guardBreakDamageScale: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(chainWindupSeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(stepAttackGraceSeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(overholdDrainPerSecond: value));
            if (value != 0f)
                Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(attackCost: value));
            else Assert.That(new MeleeCombatSettings(attackCost: 0f).AttackCost, Is.Zero, "Only the swing may be free.");
        }

        [Test]
        public void FrameTableInvariantsAreEnforcedByTheConstructor()
        {
            Assert.That(MeleeCombatSettings.Crowbar, Is.Not.Null);
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(staggerSeconds: S.HitRecoverySeconds),
                "A landed hit must leave the attacker free before the victim.");
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(counterHitStaggerBonus: .05f),
                "A counter-hit must guarantee the backhand.");
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(parriedRecoverySeconds: .3f),
                "A parry must guarantee one light punish.");
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(attackBufferSeconds: S.HitRecoverySeconds + .05f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(parryMaxPower: 1.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(guardBreakDamageScale: 1.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(chainWindupSeconds: S.WindupSeconds),
                "A backhand as slow as the full swing cannot be guaranteed after a counter-hit.");
            Assert.That(new MeleeCombatSettings(chainWindupSeconds: .1f).ChainWindupSeconds, Is.EqualTo(.1f).Within(Eps));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidTimeCannotCorruptState(float seconds)
        {
            MeleeCombatant actor = new MeleeCombatant();
            Assert.Throws<ArgumentOutOfRangeException>(() => actor.Advance(seconds));
            Assert.Throws<ArgumentOutOfRangeException>(() => actor.AnimationProgressAt(seconds));
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
        }
    }
}
