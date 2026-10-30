using System;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CombatRulesTests
    {
        private static MeleeCombatSettings S => MeleeCombatSettings.Crowbar;
        private const float Eps = .0001f;

        [TestCase(MeleeHitResult.Hit)]
        [TestCase(MeleeHitResult.Blocked)]
        [TestCase(MeleeHitResult.Parried)]
        public void ZombieCannotChainOrShortenItsLongReturn(MeleeHitResult result)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(hitRecoverySeconds: .95f),
                "Human frame advantage still rejects a return longer than the target's stagger.");
            var zombie = new MeleeCombatant(MeleeCombatSettings.ZombieUnarmed);
            zombie.SetBodyCapabilities(false, true, true);
            Assert.That(zombie.RequestAttack(), Is.True, "The remaining left hand owns a full unarmed attack.");
            Assert.That(zombie.Swing, Is.EqualTo(MeleeSwing.Backhand));
            zombie.Advance(zombie.AttackWindupSeconds + .01f);
            Assert.That(zombie.RecordAttackOutcome(result, zombie.AttackSequence), Is.True);
            zombie.Advance(Math.Max(0f, zombie.AttackActiveEnd - zombie.AttackElapsed) + .001f);
            Assert.That(zombie.Phase, Is.EqualTo(MeleePhase.Recovery));
            Assert.That(zombie.AttackRecoverySeconds, Is.EqualTo(2.4f).Within(Eps));
            Assert.That(zombie.RequestAttack(true), Is.False);
            Assert.That(zombie.TryStartRecoveryAttack(), Is.False);
            Assert.That(zombie.HasBufferedAttack || zombie.IsChained, Is.False);
            zombie.Advance(2.39f);
            Assert.That(zombie.Phase, Is.EqualTo(MeleePhase.Recovery));
            zombie.Advance(.02f);
            Assert.That(zombie.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(zombie.Stamina, Is.EqualTo(zombie.Settings.MaxStamina));
            Assert.That(zombie.RequestAttack(), Is.True);
        }

        /// <summary>Let a fresh parry expire while the input remains held.</summary>
        private static void HoldGuard(MeleeCombatant actor)
        {
            actor.SetBlocking(true);
            actor.Advance(S.ParryWindowSeconds + .01f);
        }

        /// <summary>Spend ordinary movement effort and finish its complete travel/recovery.</summary>
        private static float SpendStep(MeleeCombatant actor)
        {
            Assert.That(actor.TryStartStep(), Is.True);
            actor.Advance(S.StepDurationSeconds);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            return actor.Stamina;
        }

        [TestCase(MeleeBodyRegion.Torso, MeleeHitSide.Front, 25f)]
        [TestCase(MeleeBodyRegion.Torso, MeleeHitSide.Rear, 31.25f)]
        [TestCase(MeleeBodyRegion.Head, MeleeHitSide.Front, 50f)]
        [TestCase(MeleeBodyRegion.Head, MeleeHitSide.Left, 50f)]
        [TestCase(MeleeBodyRegion.Head, MeleeHitSide.Right, 50f)]
        [TestCase(MeleeBodyRegion.Head, MeleeHitSide.Top, 50f)]
        [TestCase(MeleeBodyRegion.Head, MeleeHitSide.Bottom, 50f)]
        [TestCase(MeleeBodyRegion.Head, MeleeHitSide.Rear, 100f)]
        [TestCase(MeleeBodyRegion.LeftArm, MeleeHitSide.Rear, 12.5f)]
        [TestCase(MeleeBodyRegion.RightArm, MeleeHitSide.Front, 12.5f)]
        [TestCase(MeleeBodyRegion.LeftLeg, MeleeHitSide.Front, 18.75f)]
        [TestCase(MeleeBodyRegion.RightLeg, MeleeHitSide.Rear, 18.75f)]
        public void AnatomicalDamageTableAppliesToTheFixedCrowbarHit(MeleeBodyRegion region,
            MeleeHitSide side, float damage)
        {
            var actor = new MeleeCombatant();
            var location = new MeleeHitLocation(region, side);
            Assert.That(location.IsCritical, Is.EqualTo(region == MeleeBodyRegion.Head));
            Assert.That(location.IsFinisher, Is.EqualTo(region == MeleeBodyRegion.Head && side == MeleeHitSide.Rear));
            Assert.That(MeleeDamageProfile.Crowbar.ResolveDamage(S.Damage, S.MaxHealth, location),
                Is.EqualTo(damage).Within(Eps));
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, false, 0f, location), Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth - damage).Within(Eps));
            Assert.That(actor.IsDefeated, Is.EqualTo(location.IsFinisher));
        }

        [TestCase(25f, 100f, 50f)]
        [TestCase(40f, 100f, 80f)]
        [TestCase(1000f, 100f, 99f)]
        [TestCase(1000f, 200f, 198f)]
        public void AnatomicalNormalHeadSurvivesAtFullHealthButCanFinishAnInjuredTarget(
            float damage, float maxHealth, float expectedDamage)
        {
            var settings = new MeleeCombatSettings(maxHealth: maxHealth);
            var actor = new MeleeCombatant(settings);
            var head = new MeleeHitLocation(MeleeBodyRegion.Head, MeleeHitSide.Front);
            actor.ReceiveHit(damage, S.BlockCost, false, 1f, head);
            Assert.That(actor.Health, Is.EqualTo(maxHealth - expectedDamage).Within(Eps));
            Assert.That(actor.IsDefeated, Is.False, "The ordinary head must survive one hit from full HP, including future tuning.");
            actor.Reset();
            actor.ReceiveHit(maxHealth - expectedDamage * .5f, S.BlockCost, false);
            Assert.That(actor.Health, Is.GreaterThan(0f));
            actor.ReceiveHit(damage, S.BlockCost, false, 1f, head);
            Assert.That(actor.Health, Is.Zero, "The cap is based on maximum HP; repeated head hits cannot preserve one HP forever.");
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Defeated));
        }

        [TestCase(MeleeHitSide.Front)]
        [TestCase(MeleeHitSide.Rear)]
        public void AnatomicalHeadDamageRespectsTimedParryBeforeOrdinaryOrFinishingDamage(MeleeHitSide side)
        {
            var actor = new MeleeCombatant();
            var head = new MeleeHitLocation(MeleeBodyRegion.Head, side);
            actor.SetBlocking(true);
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true, 0f, head), Is.EqualTo(MeleeHitResult.Parried));
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth));
            actor.Reset();
            HoldGuard(actor);
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true, 0f, head), Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(actor.Health, Is.EqualTo(head.IsFinisher ? 0f : S.MaxHealth - 50f).Within(Eps));
            Assert.That(actor.IsDefeated, Is.EqualTo(head.IsFinisher));
        }

        [TestCase(0f, 0f, -1f, MeleeHitSide.Rear)]
        [TestCase(1f, 0f, -1f, MeleeHitSide.Rear)]
        [TestCase(-1f, 0f, -1f, MeleeHitSide.Rear)]
        [TestCase(0f, 1f, -1f, MeleeHitSide.Rear)]
        [TestCase(1.0001f, 0f, -1f, MeleeHitSide.Right)]
        [TestCase(-1.0001f, 0f, -1f, MeleeHitSide.Left)]
        [TestCase(0f, 1.0001f, -1f, MeleeHitSide.Top)]
        [TestCase(0f, -1.0001f, -1f, MeleeHitSide.Bottom)]
        [TestCase(1f, 1f, -1f, MeleeHitSide.Right)]
        [TestCase(0f, 0f, 1f, MeleeHitSide.Front)]
        [TestCase(1f, 0f, 1f, MeleeHitSide.Front)]
        [TestCase(1.0001f, 0f, 1f, MeleeHitSide.Right)]
        [TestCase(0f, 1f, 0f, MeleeHitSide.Top)]
        [TestCase(0f, -1f, 0f, MeleeHitSide.Bottom)]
        [TestCase(0f, 0f, 0f, MeleeHitSide.Front)]
        [TestCase(float.MaxValue, 0f, -float.MaxValue, MeleeHitSide.Rear)]
        [TestCase(float.Epsilon, 0f, -float.Epsilon, MeleeHitSide.Rear)]
        public void AnatomicalSurfaceConesHaveStableThreeDimensionalBoundaries(
            float right, float up, float forward, MeleeHitSide expected)
        {
            var head = MeleeHitLocation.FromLocalSurface(MeleeBodyRegion.Head, right, up, forward);
            Assert.That(head.Region, Is.EqualTo(MeleeBodyRegion.Head));
            Assert.That(head.Side, Is.EqualTo(expected));
            Assert.That(head.IsFinisher, Is.EqualTo(expected == MeleeHitSide.Rear));
            var torso = MeleeHitLocation.FromLocalSurface(MeleeBodyRegion.Torso, right, up, forward);
            Assert.That(torso.Side, Is.EqualTo(expected));
            Assert.That(torso.IsFinisher || torso.IsCritical, Is.False, "The rear surface of a torso is never a head finisher.");
        }

        [Test]
        public void AnatomicalZeroDamageNeverTriggersAFinisherAndInvalidInputsCannotChangeState()
        {
            var actor = new MeleeCombatant();
            var rearHead = new MeleeHitLocation(MeleeBodyRegion.Head, MeleeHitSide.Rear);
            Assert.That(MeleeDamageProfile.Crowbar.ResolveDamage(0f, S.MaxHealth, rearHead), Is.Zero);
            Assert.That(actor.ReceiveHit(0f, S.BlockCost, false, 0f, rearHead), Is.EqualTo(MeleeHitResult.Ignored));
            foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => MeleeDamageProfile.Crowbar.ResolveDamage(invalid, S.MaxHealth, rearHead));
                Assert.Throws<ArgumentOutOfRangeException>(() => MeleeDamageProfile.Crowbar.ResolveDamage(S.Damage, invalid, rearHead));
                Assert.Throws<ArgumentOutOfRangeException>(() => actor.ReceiveHit(invalid, S.BlockCost, false, 0f, rearHead));
                if (invalid == -1f) continue;
                Assert.Throws<ArgumentOutOfRangeException>(() => MeleeHitLocation.FromLocalSurface(MeleeBodyRegion.Head, invalid, 0f, 0f));
                Assert.Throws<ArgumentOutOfRangeException>(() => MeleeHitLocation.FromLocalSurface(MeleeBodyRegion.Head, 0f, invalid, 0f));
                Assert.Throws<ArgumentOutOfRangeException>(() => MeleeHitLocation.FromLocalSurface(MeleeBodyRegion.Head, 0f, 0f, invalid));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => MeleeDamageProfile.Crowbar.ResolveDamage(S.Damage, 0f, rearHead));
            foreach (int invalid in new[] { -1, 6 })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeHitLocation((MeleeBodyRegion)invalid, MeleeHitSide.Front));
                Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeHitLocation(MeleeBodyRegion.Head, (MeleeHitSide)invalid));
                Assert.Throws<ArgumentOutOfRangeException>(() => MeleeHitLocation.FromLocalSurface((MeleeBodyRegion)invalid, 0f, 0f, 1f));
            }
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(default(MeleeHitLocation).Region, Is.EqualTo(MeleeBodyRegion.Torso));
            Assert.That(default(MeleeHitLocation).Side, Is.EqualTo(MeleeHitSide.Front));
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, false), Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth - S.Damage), "Existing callers retain ordinary torso damage.");
        }

        [TestCase(MeleePhase.Windup)]
        [TestCase(MeleePhase.Active)]
        [TestCase(MeleePhase.Recovery)]
        public void CommittedStrikeIsFixedAndRejectsChargeChainsAndQueuedGuard(MeleePhase phase)
        {
            var actor = new MeleeCombatant();
            Assert.That(actor.RequestCharge() || actor.RequestRecoveryCharge() || actor.TryStartRecoveryCharge(), Is.False);
            Assert.That(actor.RequestAttack(), Is.True);
            float elapsed = phase == MeleePhase.Windup ? .1f : phase == MeleePhase.Active
                ? S.WindupSeconds + .01f : S.WindupSeconds + S.ActiveSeconds + .01f;
            actor.Advance(elapsed);
            Assert.That(actor.Phase, Is.EqualTo(phase));
            Assert.That(actor.RequestAttack(true) || actor.TryStartRecoveryAttack() || actor.RequestCharge(), Is.False);
            Assert.That(actor.HasBufferedAttack || actor.IsCharging || actor.IsChained || actor.IsContinuation, Is.False);
            Assert.That(actor.AttackPower, Is.Zero);
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina));
            actor.SetBlocking(true);
            Assert.That(actor.IsBlocking, Is.False);
            actor.Advance(actor.CurrentAttackDurationSeconds - actor.AttackElapsed + Eps);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            actor.SetBlocking(true);
            Assert.That(actor.IsBlocking, Is.False, "A press rejected during a swing cannot become a late parry.");
            Assert.That(actor.ReleaseCharge(), Is.False);
            Assert.That(actor.RequestAttack(), Is.True);
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Forehand));
            Assert.That(actor.AttackWindupSeconds, Is.EqualTo(S.WindupSeconds));
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
            float spent = SpendStep(fine);
            SpendStep(hitch);
            fine.TryStartAttack();
            hitch.TryStartAttack();
            for (int i = 0; i < 200; i++) fine.Advance(0.01f);
            hitch.Advance(2f);
            Assert.That(fine.Phase, Is.EqualTo(hitch.Phase));
            Assert.That(fine.Stamina, Is.EqualTo(hitch.Stamina).Within(Eps));
            float start = S.StepDurationSeconds;
            float regenFrom = Math.Max(S.RegenerationDelaySeconds,
                start + S.WindupSeconds + S.ActiveSeconds);
            Assert.That(hitch.Stamina, Is.EqualTo(Math.Min(S.MaxStamina, spent + (start + 2f - regenFrom) * S.StaminaPerSecond)).Within(Eps));
        }

        [TestCase("fresh", true)]
        [TestCase("quick_release", true)]
        [TestCase("held_expired", false)]
        [TestCase("rear", false)]
        [TestCase("restored_hold", false)]
        [TestCase("stunned_press", false)]
        public void OnlyAFreshUnconsumedFrontParryPreventsDamage(string input, bool parried)
        {
            var actor = new MeleeCombatant();
            if (input == "stunned_press") actor.ReceiveShove();
            actor.SetBlocking(true, freshPress: input != "restored_hold");
            if (input == "quick_release") actor.SetBlocking(false);
            if (input == "held_expired")
            {
                actor.Advance(S.ParryWindowSeconds + Eps);
                actor.SetBlocking(true);
            }
            if (input == "stunned_press") actor.Advance(.17f);
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, input != "rear"),
                Is.EqualTo(parried ? MeleeHitResult.Parried : MeleeHitResult.Hit));
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth - (parried ? 0f : S.Damage)));
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina));
            Assert.That(actor.IsBlocking, Is.False, "A successful parry consumes this press.");
        }

        [Test]
        public void ParryCostsNothingAndDoesNotDelayRegeneration()
        {
            var actor = new MeleeCombatant();
            SpendStep(actor);
            actor.Advance(S.RegenerationDelaySeconds - S.StepDurationSeconds + .01f);
            float before = actor.Stamina;
            actor.SetBlocking(true);
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.Parried));
            Assert.That(actor.Stamina, Is.EqualTo(before));
            actor.SetBlocking(false);
            actor.Advance(.1f);
            Assert.That(actor.Stamina, Is.EqualTo(Math.Min(S.MaxStamina, before + .1f * S.StaminaPerSecond)).Within(Eps));
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
            Assert.That(actor.AttackRecoverySeconds, Is.EqualTo(S.AnimationRecoverySeconds).Within(Eps));
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
            Assert.That(actor.ActionRemaining, Is.EqualTo(Math.Max(S.StaggerSeconds, actor.Settings.AnimationRecoverySeconds - .07f)).Within(Eps), "Received damage cannot shorten the committed return.");

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

        }

        private static void AdvanceBy(MeleeCombatant actor, float seconds, bool hitch)
        {
            if (hitch) { actor.Advance(seconds); return; }
            for (float t = 0f; t < seconds - Eps; t += .05f) actor.Advance(Math.Min(.05f, seconds - t));
        }

        [TestCase(MeleePhase.Recovery, MeleeBufferedAction.Step)]
        [TestCase(MeleePhase.Stagger, MeleeBufferedAction.Step)]
        [TestCase(MeleePhase.Step, MeleeBufferedAction.Step)]
        [TestCase(MeleePhase.Kicking, MeleeBufferedAction.Step)]
        public void BufferedInputFromStunTailsFiresOnce(MeleePhase phase, MeleeBufferedAction action)
        {
            var actor = new MeleeCombatant();
            switch (phase)
            {
                case MeleePhase.Recovery:
                    actor.TryStartAttack();
                    actor.Advance(actor.CurrentAttackDurationSeconds - .3f);
                    Assert.That(Request(actor, action), Is.EqualTo(action != MeleeBufferedAction.Step),
                        "A step keeps its final buffer window through the weapon return.");
                    actor.Advance(.2f);
                    break;
                case MeleePhase.Stagger:
                    actor.ReceiveHit(S.Damage, S.BlockCost, false);
                    actor.Advance(S.StaggerSeconds - .1f);
                    break;
                case MeleePhase.Step:
                    actor.TryStartStep();
                    actor.Advance(S.StepDurationSeconds - .1f);
                    break;
                case MeleePhase.Kicking:
                    actor.TryStartKick();
                    actor.Advance(actor.CurrentKickDurationSeconds - .1f);
                    break;
            }
            Assert.That(actor.Phase, Is.EqualTo(phase));
            Assert.That(actor.ActionRemaining, Is.EqualTo(.1f).Within(Eps));
            float stamina = actor.Stamina;
            // Kicking already spent effort, so its last return restores breath
            // before the queued step pays. The original rows start with a full meter.
            float stepStartStamina = phase == MeleePhase.Kicking
                ? Math.Min(S.MaxStamina, stamina + actor.ActionRemaining * S.StaminaPerSecond) : stamina;
            int sequence = actor.AttackSequence;
            Assert.That(Request(actor, action), Is.True);
            Assert.That(Request(actor, action), Is.True, "Repeated presses share one pending slot.");
            Assert.That(actor.BufferedAction, Is.EqualTo(action));
            Assert.That(actor.Stamina, Is.EqualTo(stamina).Within(Eps), "Queueing does not spend before the action starts.");
            actor.Advance(.15f);
            float startedElapsed = .05f;
            if (phase == MeleePhase.Recovery && action != MeleeBufferedAction.Step)
            {
                Assert.That(actor.AttackSequence, Is.EqualTo(sequence), "Swing input waits for the post-contact handoff.");
                Assert.That(actor.TryContinueAttack(), Is.True);
                startedElapsed = 0f;
            }
            Assert.That(actor.BufferedAction, Is.EqualTo(MeleeBufferedAction.None));
            Assert.That(actor.AttackSequence, Is.EqualTo(sequence + 1));
            switch (action)
            {
                case MeleeBufferedAction.Attack:
                    Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Windup));
                    Assert.That(actor.AttackElapsed, Is.EqualTo(startedElapsed).Within(Eps));
                    break;
                case MeleeBufferedAction.Step:
                    Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Step));
                    Assert.That(actor.StepElapsed, Is.EqualTo(.05f).Within(Eps));
                    Assert.That(actor.Stamina, Is.EqualTo(stepStartStamina - S.StepCost).Within(Eps));
                    break;
            }
            actor.Advance(3f);
            Assert.That(actor.AttackSequence, Is.EqualTo(sequence + 1), "One press never repeats itself.");
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
        }

        private static bool Request(MeleeCombatant actor, MeleeBufferedAction action) => action switch
        {
            MeleeBufferedAction.Attack => actor.RequestAttack(),
            MeleeBufferedAction.Charge => actor.RequestCharge(),
            MeleeBufferedAction.Kick => actor.RequestKick(),
            MeleeBufferedAction.Shove => actor.RequestShove(),
            _ => actor.RequestStep()
        };

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
                Assert.That(actor.RequestAttack(), Is.False);
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

        [TestCase(MeleeAttackOutcome.Hit)]
        [TestCase(MeleeAttackOutcome.Blocked)]
        [TestCase(MeleeAttackOutcome.Miss)]
        [TestCase(MeleeAttackOutcome.Obstacle)]
        [TestCase(MeleeAttackOutcome.Parried)]
        public void ContactOutcomeOwnsRecoveryButKeepsTheAuthoredAnimationEndpoint(MeleeAttackOutcome outcome)
        {
            float recovery = (outcome switch
            {
                MeleeAttackOutcome.Hit => S.AnimationRecoverySeconds,
                MeleeAttackOutcome.Blocked => S.BlockRecoverySeconds,
                MeleeAttackOutcome.Obstacle => S.ObstacleRecoverySeconds,
                MeleeAttackOutcome.Parried => S.ParriedRecoverySeconds,
                _ => S.AnimationRecoverySeconds
            });
            var actor = new MeleeCombatant();
            actor.TryStartAttack();
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
            Assert.That(actor.AttackRecoverySeconds, Is.EqualTo(S.AnimationRecoverySeconds).Within(Eps));
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
            float spent = SpendStep(fine);
            SpendStep(hitch);
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
            float start = S.StepDurationSeconds;
            float regenFrom = Math.Max(S.RegenerationDelaySeconds,
                start + S.WindupSeconds + S.ActiveSeconds);
            Assert.That(hitch.Stamina, Is.EqualTo(Math.Min(S.MaxStamina, spent + (start + 2f - regenFrom) * S.StaminaPerSecond)).Within(Eps),
                "Recovery length no longer decides when breath returns; the end of the arc does.");
        }

        [Test]
        public void UnblockedHitInterruptsAttackButNeverDelaysRegeneration()
        {
            var actor = new MeleeCombatant();
            float spent = SpendStep(actor);
            actor.TryStartAttack();
            MeleeAdvanceResult step = actor.Advance(.5f);
            Assert.That(step.HasActiveWindow, Is.True);
            actor.ReceiveHit(S.Damage, S.BlockCost, false);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Stagger));
            Assert.That(actor.TryRegisterHit(1, step.AttackSequence), Is.False);
            actor.Advance(.5f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            float regenFrom = Math.Max(S.RegenerationDelaySeconds, S.StepDurationSeconds + .5f);
            Assert.That(actor.Stamina, Is.EqualTo(Math.Min(S.MaxStamina,
                spent + (S.StepDurationSeconds + 1f - regenFrom) * S.StaminaPerSecond)).Within(Eps));
        }

        [Test]
        public void ExhaustionRejectsStepButNotAttackOrParryAndResetInvalidatesOldContacts()
        {
            var actor = new MeleeCombatant();
            actor.RestoreVitals(S.MaxHealth, 0f);
            Assert.That(actor.TryStartStep(), Is.False);
            actor.SetBlocking(true);
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.Parried));
            actor.Advance(S.ParryImpactSeconds + Eps);
            Assert.That(actor.TryStartAttack(), Is.True);
            int obsolete = actor.AttackSequence;
            actor.ReceiveHit(S.MaxHealth, S.BlockCost, false);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Defeated));
            Assert.That(actor.TryStartAttack() || actor.TryStartStep(), Is.False);
            float stamina = actor.Stamina;
            actor.Advance(20f);
            Assert.That(actor.Stamina, Is.EqualTo(stamina));
            actor.Reset();
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth));
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina));
            Assert.That(actor.IsBlocking || actor.IsCharging, Is.False);
            actor.TryStartAttack();
            actor.Advance(.5f);
            Assert.That(actor.TryRegisterHit(1, obsolete), Is.False);
        }

        [Test]
        public void CancellationPreservesSpentEffortAndCannotReplayTheOldSwing()
        {
            var actor = new MeleeCombatant();
            float spent = SpendStep(actor);
            actor.TryStartAttack();
            MeleeAdvanceResult step = actor.Advance(.1f);
            actor.SetBlocking(true);
            actor.CancelAction();
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth));
            Assert.That(actor.Stamina, Is.EqualTo(spent).Within(Eps));
            Assert.That(actor.IsBlocking, Is.False);
            Assert.That(actor.TryRegisterHit(1, step.AttackSequence), Is.False);
            Assert.That(actor.Advance(.1f).HasActiveWindow, Is.False);
            actor.ReceiveHit(S.MaxHealth, S.BlockCost, false);
            actor.CancelAction();
            Assert.That(actor.IsDefeated, Is.True);
        }

        [Test]
        public void ParryConsumesOnePressAndRequiresReleaseAndRearm()
        {
            var actor = new MeleeCombatant();
            actor.SetBlocking(true);
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.Parried));
            Assert.That(actor.IsBlocking, Is.False);
            Assert.That(actor.TryStartAttack(), Is.False, "The impact has a brief committed reaction.");
            actor.Advance(S.ParryImpactSeconds + Eps);
            actor.SetBlocking(false);
            actor.SetBlocking(true);
            Assert.That(actor.IsBlocking, Is.False, "An early repress cannot rearm the action.");
            actor.Advance(S.ParryRearmSeconds);
            actor.SetBlocking(true);
            Assert.That(actor.IsBlocking, Is.False, "Waiting with the button held cannot repeat it.");
            actor.SetBlocking(false);
            actor.SetBlocking(true);
            Assert.That(actor.ReceiveUnarmedHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.Parried));
            Assert.That(actor.ReceiveHit(S.Damage, S.BlockCost, true), Is.EqualTo(MeleeHitResult.Hit),
                "The same press cannot protect a second incoming contact.");
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
            Assert.That(actor.RequestAttack(), Is.False, "A strike cannot queue behind step travel.");
            Assert.That(actor.CancelBufferedAction(MeleeBufferedAction.Attack), Is.False);
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
            Assert.That(actor.Stamina, Is.EqualTo(S.MaxStamina - S.StepCost), "The step completes before its regeneration delay.");
            Assert.That(actor.IsBlocking, Is.False, "A held input cannot create a late parry after the step.");
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

        [TestCase(MeleePhase.Ready)]
        [TestCase(MeleePhase.Windup)]
        public void CloseShoveReplacesOnlyAnUncommittedSwingAndNeverOpensAWeaponWindow(MeleePhase source)
        {
            var actor = new MeleeCombatant();
            if (source == MeleePhase.Windup) actor.TryStartAttack();
            else actor.SetBlocking(true);
            float stamina = actor.Stamina;
            int sequence = actor.AttackSequence;

            if (source == MeleePhase.Windup)
            {
                Assert.That(actor.TryStartKick(), Is.False, "Q cannot replace an already released weapon swing.");
                Assert.That(actor.Stamina, Is.EqualTo(stamina));
                Assert.That(actor.AttackSequence, Is.EqualTo(sequence));
            }
            Assert.That(actor.TryStartShove(), Is.True);
            Assert.That(actor.IsShoving, Is.True);
            Assert.That(actor.IsAttacking || actor.IsCharging || actor.IsBlocking, Is.False);
            Assert.That(actor.Charge01, Is.Zero);
            Assert.That(actor.AttackPower, Is.Zero);
            Assert.That(actor.AttackSequence, Is.EqualTo(unchecked(sequence + 1)));
            Assert.That(actor.Stamina, Is.EqualTo(stamina - S.ShoveCost).Within(Eps));
            Assert.That(actor.TryStartShove(), Is.False, "Holding contact must not pay or start twice.");
            Assert.That(actor.ReleaseCharge(), Is.False);

            MeleeAdvanceResult contact = actor.Advance(S.ShoveContactSeconds);
            Assert.That(contact.HasActiveWindow, Is.False);
            Assert.That(actor.ShoveElapsed, Is.EqualTo(S.ShoveContactSeconds).Within(Eps));
            Assert.That(actor.TryRegisterHit(1, actor.AttackSequence), Is.False);
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth));
            actor.Advance(S.ShoveDurationSeconds - S.ShoveContactSeconds - .05f);
            Assert.That(actor.RequestAttack(), Is.False, "The weapon cannot queue behind another action.");
            Assert.That(actor.CancelBufferedAction(MeleeBufferedAction.Attack), Is.False);
            Assert.That(actor.BufferedAction, Is.EqualTo(MeleeBufferedAction.None));
            Assert.That(actor.Advance(.06f).HasActiveWindow, Is.False);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.ShoveProgress, Is.EqualTo(1f));
            Assert.That(actor.Stamina, Is.EqualTo(stamina - S.ShoveCost).Within(Eps));

            if (source == MeleePhase.Windup)
            {
                Assert.That(actor.TryStartAttack(), Is.False, "Converting the windup cannot erase its weapon clock.");
                actor.Advance(actor.CooldownRemaining(MeleeBufferedAction.Attack) + .01f);
            }
            Assert.That(actor.TryStartAttack(), Is.True);
            Assert.That(actor.ShoveElapsed, Is.Zero, "A later action cannot replay the old contact.");
            actor.Advance(S.WindupSeconds + .01f);
            Assert.That(actor.TryStartShove(), Is.False, "An already live weapon arc cannot turn into a shove.");
        }

        [TestCase("cancel")]
        [TestCase("damage")]
        [TestCase("knockdown")]
        [TestCase("reset")]
        public void InterruptedShoveCannotRetainAContactOrReturnAfterRecovery(string interruption)
        {
            var actor = new MeleeCombatant();
            actor.TryStartShove();
            actor.Advance(S.ShoveContactSeconds * .5f);
            switch (interruption)
            {
                case "cancel": actor.CancelAction(); break;
                case "damage": actor.ReceiveHit(S.Damage, S.BlockCost, false); break;
                case "knockdown": actor.BeginKnockdown(); break;
                case "reset": actor.Reset(); break;
            }
            Assert.That(actor.IsShoving, Is.False);
            Assert.That(actor.ShoveElapsed, Is.Zero);
            Assert.That(actor.Advance(2f).HasActiveWindow, Is.False);
            Assert.That(actor.IsShoving, Is.False);
            Assert.That(actor.ShoveElapsed, Is.Zero);
        }

        [Test]
        public void ShoveContactClockSurvivesAHitchAndUnaffordableShovePreservesState()
        {
            var fine = new MeleeCombatant();
            var hitch = new MeleeCombatant();
            fine.TryStartShove();
            hitch.TryStartShove();
            for (int i = 0; i < 100; i++) Assert.That(fine.Advance(.01f).HasActiveWindow, Is.False);
            Assert.That(hitch.Advance(1f).HasActiveWindow, Is.False);
            Assert.That(hitch.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(hitch.ShoveElapsed, Is.EqualTo(S.ShoveDurationSeconds).Within(Eps),
                "Runtime needs the terminal contact sample even after a whole-action hitch.");
            Assert.That(hitch.ShoveElapsed, Is.EqualTo(fine.ShoveElapsed).Within(Eps));
            Assert.That(hitch.Stamina, Is.EqualTo(fine.Stamina).Within(Eps));

            var exhausted = new MeleeCombatant();
            exhausted.RestoreVitals(S.MaxHealth, 0f);
            int sequence = exhausted.AttackSequence;
            Assert.That(exhausted.TryStartShove(), Is.False);
            Assert.That(exhausted.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(exhausted.Stamina, Is.Zero);
            Assert.That(exhausted.AttackSequence, Is.EqualTo(sequence));
        }

        [TestCase(MeleePhase.Ready)]
        [TestCase(MeleePhase.Windup)]
        [TestCase(MeleePhase.Shoving)]
        [TestCase(MeleePhase.Kicking)]
        public void ReceivedShoveInterruptsIntentWithoutHealthOrStaminaDamage(MeleePhase source)
        {
            var actor = new MeleeCombatant();
            switch (source)
            {
                case MeleePhase.Ready: actor.SetBlocking(true); break;
                case MeleePhase.Windup: actor.TryStartAttack(); break;
                case MeleePhase.Shoving: actor.TryStartShove(); actor.Advance(.04f); break;
                case MeleePhase.Kicking: actor.TryStartKick(); actor.Advance(.04f); break;
            }
            float stamina = actor.Stamina;
            int sequence = actor.AttackSequence;
            Assert.That(actor.ReceiveShove(), Is.True);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Stagger));
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth));
            Assert.That(actor.Stamina, Is.EqualTo(stamina));
            Assert.That(actor.IsBlocking || actor.IsCharging || actor.IsShoving, Is.False);
            Assert.That(actor.IsKicking, Is.False);
            Assert.That(actor.KickElapsed, Is.Zero);
            Assert.That(actor.ShoveElapsed, Is.Zero);
            Assert.That(actor.BufferedAction, Is.EqualTo(MeleeBufferedAction.None));
            Assert.That(actor.TryRegisterHit(1, sequence), Is.False);
            Assert.That(actor.ActionRemaining, Is.EqualTo(.16f).Within(Eps));
            actor.Advance(.17f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));

            actor.ReceiveHit(S.Damage, S.BlockCost, false);
            float previousStun = actor.ActionRemaining;
            Assert.That(actor.ReceiveShove(), Is.True);
            Assert.That(actor.ActionRemaining, Is.EqualTo(previousStun).Within(Eps),
                "A shove cannot shorten a previous weapon stagger.");
            actor.BeginKnockdown();
            Assert.That(actor.ReceiveShove(), Is.False);
            actor.BeginRise();
            Assert.That(actor.ReceiveShove(), Is.False);
            actor.Reset();
            actor.ReceiveHit(S.MaxHealth, S.BlockCost, false);
            Assert.That(actor.ReceiveShove(), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void KickCommitsOnceFromReadyOrParryAndUnaffordableKickPreservesState(bool parrying)
        {
            var actor = new MeleeCombatant();
            if (parrying) actor.SetBlocking(true);
            float stamina = actor.Stamina;
            int sequence = actor.AttackSequence;
            Assert.That(actor.TryStartKick(), Is.True);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Kicking));
            Assert.That(actor.AttackSequence, Is.EqualTo(sequence + 1));
            Assert.That(actor.Stamina, Is.EqualTo(stamina - S.KickCost).Within(Eps));
            Assert.That(actor.IsBlocking || actor.IsCharging || actor.IsAttacking || actor.IsContinuation || actor.IsChained, Is.False);
            Assert.That(actor.Charge01, Is.Zero);
            Assert.That(actor.ReleaseCharge(), Is.False);
            Assert.That(actor.TryStartKick() || actor.TryStartShove() || actor.TryStartStep(), Is.False);
            Assert.That(actor.RequestAttack(), Is.False, "The fixed strike cannot queue behind a kick.");
            Assert.That(actor.Stamina, Is.EqualTo(stamina - S.KickCost).Within(Eps));
            Assert.That(actor.Advance(S.KickWindupSeconds - .01f).HasActiveWindow, Is.False);
            Assert.That(actor.TryRegisterKickHit(1, actor.AttackSequence), Is.False);
            Assert.That(actor.KickProgress, Is.EqualTo(actor.KickElapsed / S.KickAnimationDurationSeconds).Within(Eps));
            Assert.That(actor.KickAnimationSecondsAt(0f), Is.Zero);
            Assert.That(actor.KickAnimationSecondsAt(S.KickWindupSeconds * .5f), Is.EqualTo(.15f).Within(Eps));
            Assert.That(actor.KickAnimationSecondsAt(S.KickWindupSeconds), Is.EqualTo(.30f).Within(Eps));
            Assert.That(actor.KickAnimationSecondsAt(actor.KickActiveEnd), Is.EqualTo(.40f).Within(Eps));
            Assert.That(actor.KickAnimationSecondsAt(actor.CurrentKickDurationSeconds), Is.EqualTo(.95f).Within(Eps));
            Assert.That(actor.KickAnimationSecondsAt(actor.CurrentKickDurationSeconds + 1f), Is.EqualTo(.95f).Within(Eps));

            var exhausted = new MeleeCombatant();
            exhausted.RestoreVitals(S.MaxHealth, 0f);
            sequence = exhausted.AttackSequence;
            Assert.That(exhausted.TryStartKick(), Is.False);
            Assert.That(exhausted.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(exhausted.Charge01, Is.Zero);
            Assert.That(exhausted.AttackSequence, Is.EqualTo(sequence));
            Assert.That(exhausted.Stamina, Is.Zero);
        }

        [TestCase(.5f, 0f)]
        [TestCase(1f, 1f)]
        [TestCase(1f, 2f)]
        public void KickContactWindowSurvivesHitchesAndAcceptsOnlyItsFirstTarget(float activeEnd, float recoveryFraction)
        {
            var actor = new MeleeCombatant();
            actor.TryStartKick();
            int sequence = actor.AttackSequence;
            float seconds = S.KickWindupSeconds + S.KickActiveSeconds * activeEnd + S.KickMissRecoverySeconds * recoveryFraction;
            MeleeAdvanceResult contact = actor.Advance(seconds);
            Assert.That(contact.IsKick && contact.HasActiveWindow, Is.True);
            Assert.That(contact.AttackSequence, Is.EqualTo(sequence));
            Assert.That(contact.ActiveStartNormalized, Is.EqualTo(0f).Within(Eps));
            Assert.That(contact.ActiveEndNormalized, Is.EqualTo(activeEnd).Within(Eps));
            Assert.That(actor.TryRegisterHit(1, sequence), Is.False, "A sole must never open weapon damage.");
            Assert.That(actor.RecordAttackOutcome(MeleeHitResult.Hit, sequence), Is.False);
            Assert.That(actor.TryRegisterKickHit(1, sequence - 1), Is.False);
            Assert.That(actor.TryRegisterKickHit(1, sequence), Is.True);
            Assert.That(actor.TryRegisterKickHit(1, sequence), Is.False, "Body colliders share one contact.");
            Assert.That(actor.TryRegisterKickHit(2, sequence), Is.False, "The first real contact stops further target grants.");
            Assert.That(actor.RecordKickOutcome(MeleeAttackOutcome.Hit, sequence), Is.True);
            Assert.That(actor.KickOutcome, Is.EqualTo(MeleeAttackOutcome.Hit));
            Assert.That(actor.CurrentKickDurationSeconds, Is.EqualTo(actor.KickActiveEnd + S.KickHitRecoverySeconds).Within(Eps));
            actor.Advance(Math.Max(.01f, actor.KickActiveEnd - actor.KickElapsed + .01f));
            actor.Advance(.01f);
            Assert.That(actor.RecordKickOutcome(MeleeAttackOutcome.Hit, sequence), Is.False);
            Assert.That(actor.TryRegisterKickHit(2, sequence), Is.False);
        }

        [TestCase(MeleeAttackOutcome.Hit)]
        [TestCase(MeleeAttackOutcome.Miss)]
        [TestCase(MeleeAttackOutcome.Obstacle)]
        public void KickReturnCompletesBeforeTheNextFixedWeaponStrike(MeleeAttackOutcome outcome)
        {
            var actor = new MeleeCombatant();
            Assert.That(actor.TryStartKick(), Is.True);
            actor.Advance(S.KickWindupSeconds + S.KickActiveSeconds * .5f);
            int sequence = actor.AttackSequence;
            Assert.That(actor.RecordKickOutcome(outcome, sequence), Is.True);
            Assert.That(actor.KickAnimationSecondsAt(actor.KickActiveEnd + actor.KickRecoverySeconds * .5f),
                Is.EqualTo(.675f).Within(Eps));
            actor.Advance(Math.Max(0f, actor.KickActiveEnd - actor.KickElapsed) + .01f, allowBufferedAttack: false);
            Assert.That(actor.RequestAttack(), Is.False);
            Assert.That(actor.HasBufferedAttack, Is.False);
            actor.Advance(actor.ActionRemaining + Eps);
            Assert.That(actor.RequestAttack(), Is.True);
            Assert.That(actor.AttackSequence, Is.EqualTo(sequence + 1));
            Assert.That(actor.Swing, Is.EqualTo(MeleeSwing.Forehand));
            Assert.That(actor.AttackWindupSeconds, Is.EqualTo(S.WindupSeconds));
            Assert.That(actor.KickElapsed, Is.Zero);
        }

        [TestCase("cancel")]
        [TestCase("damage")]
        [TestCase("shove")]
        [TestCase("kick")]
        [TestCase("knockdown")]
        [TestCase("reset")]
        public void InterruptedKickInvalidatesContactsAndBufferedIntent(string interruption)
        {
            var actor = new MeleeCombatant();
            actor.TryStartKick();
            MeleeAdvanceResult contact = actor.Advance(S.KickWindupSeconds + .01f);
            Assert.That(contact.IsKick, Is.True);
            Assert.That(actor.TryRegisterKickHit(1, contact.AttackSequence), Is.True);
            actor.RecordKickOutcome(MeleeAttackOutcome.Hit, contact.AttackSequence);
            actor.Advance(actor.CurrentKickDurationSeconds - actor.KickElapsed - .1f);
            Assert.That(actor.RequestStep(), Is.True);
            float remaining = actor.ActionRemaining;
            switch (interruption)
            {
                case "cancel": actor.CancelAction(); break;
                case "damage": actor.ReceiveHit(S.Damage, S.BlockCost, false); break;
                case "shove": actor.ReceiveShove(); break;
                case "kick": actor.ReceiveKick(); break;
                case "knockdown": actor.BeginKnockdown(); break;
                case "reset": actor.Reset(); break;
            }
            Assert.That(actor.IsKicking, Is.False);
            Assert.That(actor.KickElapsed, Is.Zero);
            Assert.That(actor.KickOutcome, Is.EqualTo(MeleeAttackOutcome.None));
            Assert.That(actor.HasBufferedAttack || actor.HasBufferedStep, Is.False);
            Assert.That(actor.TryRegisterKickHit(1, contact.AttackSequence), Is.False);
            Assert.That(actor.RecordKickOutcome(MeleeAttackOutcome.Hit, contact.AttackSequence), Is.False);
            if (interruption == "damage")
                Assert.That(actor.ActionRemaining, Is.GreaterThanOrEqualTo(Math.Max(remaining,
                    S.StaggerSeconds + S.CounterHitStaggerBonus) - Eps), "The exposed kick is a weapon counter opportunity.");
            Assert.That(actor.Advance(3f).HasActiveWindow, Is.False);
        }

        [TestCase(MeleeBufferedAction.Step)]
        public void KickBufferWaitsAtReadyUntilRuntimeConfirmsPhysicalSupport(MeleeBufferedAction action)
        {
            var actor = new MeleeCombatant();
            actor.TryStartKick();
            actor.Advance(actor.CurrentKickDurationSeconds - .1f, allowBufferedAttack: false);
            int sequence = actor.AttackSequence;
            Assert.That(Request(actor, action), Is.True);
            Assert.That(actor.BufferedAction, Is.EqualTo(action), "No Advance may consume an intent while runtime withholds support.");
            actor.Advance(.2f, allowBufferedAttack: false);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(actor.BufferedAction, Is.EqualTo(action));
            Assert.That(actor.AttackSequence, Is.EqualTo(sequence));
            Assert.That(actor.AttackElapsed, Is.Zero);
            Assert.That(actor.StepElapsed, Is.Zero);
            Assert.That(actor.Charge01, Is.Zero);
            actor.Advance(.02f, allowBufferedAttack: false);
            Assert.That(actor.AttackSequence, Is.EqualTo(sequence), "Returning Ready still waits for the support gate.");
            Assert.That(action == MeleeBufferedAction.Step ? actor.TryContinueBufferedStep() : actor.TryContinueAttack(), Is.True);
            Assert.That(actor.AttackSequence, Is.EqualTo(sequence + 1));
            Assert.That(actor.BufferedAction, Is.EqualTo(MeleeBufferedAction.None));
            Assert.That(actor.TryContinueAttack() || actor.TryContinueBufferedStep(), Is.False, "A released buffer cannot start twice.");
            Assert.That(actor.KickElapsed, Is.Zero);
            Assert.That(actor.IsKicking, Is.False);
            Assert.That(actor.Phase, Is.EqualTo(action == MeleeBufferedAction.Step ? MeleePhase.Step :
                action == MeleeBufferedAction.Charge ? MeleePhase.Charging : MeleePhase.Windup));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReceivedKickBypassesFaceGuardWithFlatDamageAndRetainsLongerStuns(bool freshGuard)
        {
            var actor = new MeleeCombatant();
            if (freshGuard) actor.SetBlocking(true); else HoldGuard(actor);
            float stamina = actor.Stamina;
            Assert.That(actor.ReceiveKick(), Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth - S.KickDamage).Within(Eps));
            Assert.That(actor.Stamina, Is.EqualTo(stamina));
            Assert.That(actor.IsBlocking, Is.False);
            Assert.That(actor.ActionRemaining, Is.EqualTo(S.KickStaggerSeconds).Within(Eps));
            actor.Advance(S.KickStaggerSeconds + .01f);
            actor.ReceiveHit(S.Damage, S.BlockCost, false);
            float longer = actor.ActionRemaining;
            Assert.That(actor.ReceiveKick(), Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(actor.ActionRemaining, Is.EqualTo(longer).Within(Eps));
            Assert.That(actor.Health, Is.EqualTo(S.MaxHealth - 2f * S.KickDamage - S.Damage).Within(Eps));

            actor.Reset();
            HoldGuard(actor);
            actor.ReceiveHit(S.Damage, S.MaxStamina + 1f, true);
            longer = actor.ActionRemaining;
            actor.ReceiveKick();
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.GuardBroken));
            Assert.That(actor.ActionRemaining, Is.EqualTo(longer).Within(Eps));
            float health = actor.Health;
            actor.BeginKnockdown();
            Assert.That(actor.ReceiveKick(), Is.EqualTo(MeleeHitResult.Ignored));
            actor.BeginRise();
            Assert.That(actor.ReceiveKick(), Is.EqualTo(MeleeHitResult.Ignored));
            Assert.That(actor.Health, Is.EqualTo(health));
            actor.Reset();
            actor.ReceiveHit(S.MaxHealth - 2f, S.BlockCost, false);
            Assert.That(actor.ReceiveKick(), Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(actor.IsDefeated, Is.True);
            Assert.That(actor.ReceiveKick(), Is.EqualTo(MeleeHitResult.Ignored));
        }

        [TestCase(MeleeBufferedAction.Attack, MeleeBufferedAction.Kick)]
        [TestCase(MeleeBufferedAction.Attack, MeleeBufferedAction.Shove)]
        [TestCase(MeleeBufferedAction.Kick, MeleeBufferedAction.Shove)]
        [TestCase(MeleeBufferedAction.Shove, MeleeBufferedAction.Kick)]
        [TestCase(MeleeBufferedAction.Step, MeleeBufferedAction.Kick)]
        [TestCase(MeleeBufferedAction.Step, MeleeBufferedAction.Shove)]
        public void IndependentActionClocksYieldOnlyAfterResolvedContactAndSurviveSwitching(
            MeleeBufferedAction source, MeleeBufferedAction next)
        {
            var actor = new MeleeCombatant();
            bool started = source switch
            {
                MeleeBufferedAction.Attack => actor.TryStartAttack(),
                MeleeBufferedAction.Kick => actor.TryStartKick(),
                MeleeBufferedAction.Shove => actor.TryStartShove(),
                _ => actor.TryStartStep()
            };
            Assert.That(started, Is.True);
            int previousSequence = actor.AttackSequence;
            float contactEnd = source switch
            {
                MeleeBufferedAction.Attack => actor.AttackActiveEnd,
                MeleeBufferedAction.Kick => actor.KickActiveEnd,
                MeleeBufferedAction.Shove => S.ShoveActiveEndSeconds,
                _ => S.StepTravelSeconds
            };
            // A close shove intentionally replaces an uncommitted weapon
            // windup. Exercise the cross-type queue after that windup instead.
            float sourceElapsed = 0f;
            if (source == MeleeBufferedAction.Attack && next == MeleeBufferedAction.Shove)
            {
                sourceElapsed = S.WindupSeconds + .01f;
                actor.Advance(sourceElapsed, allowBufferedAttack: false);
            }
            Assert.That(Request(actor, next), Is.True);
            actor.Advance(contactEnd - sourceElapsed - .01f, allowBufferedAttack: false);
            Assert.That(actor.CanTransitionTo(next), Is.False);
            Assert.That(actor.TryContinueBufferedAction(), Is.False);
            MeleeAdvanceResult previousWindow = actor.Advance(.01f + Eps, allowBufferedAttack: false);
            Assert.That(actor.AttackSequence, Is.EqualTo(previousSequence), "Only runtime may release the contact boundary.");
            if (source == MeleeBufferedAction.Attack || source == MeleeBufferedAction.Kick)
                Assert.That(previousWindow.AttackSequence, Is.EqualTo(previousSequence));
            float previousCooldown = actor.CooldownRemaining(source);
            Assert.That(previousCooldown, Is.GreaterThan(0f), "The old return is still cooling down.");
            Assert.That(actor.CanTransitionTo(next), Is.True);
            Assert.That(actor.TryContinueBufferedAction(), Is.True);
            Assert.That(actor.AttackSequence, Is.EqualTo(previousSequence + 1));
            Assert.That(actor.BufferedAction, Is.EqualTo(MeleeBufferedAction.None));
            Assert.That(actor.CooldownRemaining(source), Is.EqualTo(previousCooldown).Within(Eps));
            Assert.That(actor.TryRegisterHit(1, previousSequence), Is.False);
            Assert.That(actor.TryRegisterKickHit(1, previousSequence), Is.False);
            actor.Advance(.025f, allowBufferedAttack: false);
            Assert.That(actor.CooldownRemaining(source), Is.EqualTo(previousCooldown - .025f).Within(Eps));
            Assert.That(actor.TryContinueBufferedAction(), Is.False, "One press cannot repeat itself.");
            if (source == MeleeBufferedAction.Step)
            {
                Assert.That(actor.CooldownRemaining(MeleeBufferedAction.Step), Is.GreaterThan(0f));
                Assert.That(actor.TryStartStep(), Is.False, "An early attack does not rearm the defensive step.");
            }
        }

        [TestCase(MeleeAttackOutcome.Hit)]
        [TestCase(MeleeAttackOutcome.Miss)]
        [TestCase(MeleeAttackOutcome.Blocked)]
        [TestCase(MeleeAttackOutcome.Obstacle)]
        [TestCase(MeleeAttackOutcome.Parried)]
        public void IndependentWeaponOutcomeClockCannotBeResetByAnotherTypeOrCancellation(MeleeAttackOutcome outcome)
        {
            var actor = new MeleeCombatant(new MeleeCombatSettings(kickWindupSeconds: .02f, kickActiveSeconds: .02f));
            actor.TryStartAttack();
            actor.Advance(actor.AttackActiveEnd + Eps, allowBufferedAttack: false);
            if (outcome == MeleeAttackOutcome.Obstacle) actor.CancelAttackOnObstacle();
            else if (outcome != MeleeAttackOutcome.Miss)
                actor.RecordAttackOutcome(outcome == MeleeAttackOutcome.Parried ? MeleeHitResult.Parried :
                    outcome == MeleeAttackOutcome.Blocked ? MeleeHitResult.Blocked : MeleeHitResult.Hit, actor.AttackSequence);
            Assert.That(actor.RequestKick(), Is.True);
            Assert.That(actor.TryContinueBufferedAction(), Is.True, "A weapon recoil owns only its own type's clock.");
            actor.Advance(actor.KickActiveEnd + Eps, allowBufferedAttack: false);
            float weaponRemaining = actor.CooldownRemaining(MeleeBufferedAction.Attack);
            Assert.That(weaponRemaining, Is.GreaterThan(0f));
            Assert.That(actor.RequestAttack(), Is.False);
            Assert.That(actor.CanTransitionTo(MeleeBufferedAction.Attack), Is.False);
            Assert.That(actor.TryContinueBufferedAction(), Is.False);
            actor.CancelAction();
            Assert.That(actor.CooldownRemaining(MeleeBufferedAction.Attack), Is.EqualTo(weaponRemaining).Within(Eps));
            Assert.That(actor.TryStartAttack(), Is.False);
            actor.Advance(weaponRemaining + Eps, allowBufferedAttack: false);
            Assert.That(actor.TryStartAttack(), Is.True);
            actor.Reset();
            foreach (MeleeBufferedAction action in new[] { MeleeBufferedAction.Attack, MeleeBufferedAction.Kick,
                MeleeBufferedAction.Shove, MeleeBufferedAction.Step })
                Assert.That(actor.CooldownRemaining(action), Is.Zero);
        }

        [TestCase(MeleeBufferedAction.Kick, false)]
        [TestCase(MeleeBufferedAction.Shove, false)]
        [TestCase(MeleeBufferedAction.Kick, true)]
        [TestCase(MeleeBufferedAction.Shove, true)]
        public void IndependentCrossTypeIntentSurvivesTheWeaponsOwnForcedRecoil(MeleeBufferedAction next, bool parried)
        {
            var actor = new MeleeCombatant();
            actor.TryStartAttack();
            actor.Advance(S.WindupSeconds + .03f, allowBufferedAttack: false);
            Assert.That(Request(actor, next), Is.True);
            Assert.That(parried ? actor.RecordAttackOutcome(MeleeHitResult.Parried, actor.AttackSequence) :
                actor.CancelAttackOnObstacle(), Is.True);
            Assert.That(actor.BufferedAction, Is.EqualTo(next));
            Assert.That(actor.CooldownRemaining(MeleeBufferedAction.Attack), Is.GreaterThan(0f));
            Assert.That(actor.TryContinueBufferedAction(), Is.True,
                "Runtime has resolved the stopped weapon and may authorize another type.");
        }

        [TestCase(MeleePhase.Stagger, MeleeBufferedAction.Attack, false)]
        [TestCase(MeleePhase.Stagger, MeleeBufferedAction.Shove, true)]
        [TestCase(MeleePhase.GuardImpact, MeleeBufferedAction.Attack, false)]
        [TestCase(MeleePhase.GuardImpact, MeleeBufferedAction.Shove, true)]
        [TestCase(MeleePhase.KnockedDown, MeleeBufferedAction.Attack, false)]
        [TestCase(MeleePhase.Rising, MeleeBufferedAction.Shove, false)]
        public void IndependentUpperBodyAttemptCanUseRecoveryWithoutRelaxingKickOrFallGates(
            MeleePhase phase, MeleeBufferedAction action, bool allowed)
        {
            var actor = new MeleeCombatant();
            switch (phase)
            {
                case MeleePhase.Stagger: actor.ReceiveShove(.7f); break;
                case MeleePhase.GuardImpact:
                    actor.SetBlocking(true); actor.ReceiveWeaponObstacle(1f); break;
                case MeleePhase.KnockedDown: actor.BeginKnockdown(); break;
                case MeleePhase.Rising: actor.BeginKnockdown(); actor.BeginRise(); break;
                case MeleePhase.Defeated: actor.ReceiveHit(S.MaxHealth, S.BlockCost, false); break;
            }
            Assert.That(actor.Phase, Is.EqualTo(phase));
            Assert.That(actor.CanTransitionTo(action), Is.False);
            Assert.That(actor.CanTransitionTo(action, allowRecoveryAttack: true), Is.EqualTo(allowed));
            Assert.That(actor.TryStartKick(), Is.False, "Recovery permission belongs only to the upper body.");
            int sequence = actor.AttackSequence;
            bool started = action switch
            {
                MeleeBufferedAction.Attack => actor.TryStartRecoveryAttack(),
                MeleeBufferedAction.Charge => actor.TryStartRecoveryCharge(),
                _ => actor.TryStartRecoveryShove()
            };
            Assert.That(started, Is.EqualTo(allowed));
            Assert.That(actor.AttackSequence, Is.EqualTo(sequence + (allowed ? 1 : 0)));
            if (!allowed) Assert.That(actor.Phase, Is.EqualTo(phase));
        }

        [Test]
        public void IndependentUpperBodyAttemptRetainsTheUnfinishedStunAndSingleKickIntent()
        {
            var actor = new MeleeCombatant();
            actor.ReceiveShove(1.4f);
            Assert.That(actor.TryStartRecoveryShove(), Is.True);
            actor.Advance(S.ShoveDurationSeconds + Eps, allowBufferedAttack: false);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Stagger));
            Assert.That(actor.ActionRemaining, Is.EqualTo(1.4f - S.ShoveDurationSeconds - Eps).Within(Eps));
            actor.Reset();
            int sequence = actor.AttackSequence;
            float stamina = actor.Stamina;
            Assert.That(actor.RequestKick(), Is.True);
            Assert.That(actor.RequestKick(), Is.True);
            actor.Advance(.3f);
            Assert.That(actor.AttackSequence, Is.EqualTo(sequence), "Only measured runtime support may start the queued kick.");
            Assert.That(actor.Stamina, Is.EqualTo(stamina));
            Assert.That(actor.CancelBufferedAction(MeleeBufferedAction.Shove), Is.False);
            Assert.That(actor.CancelBufferedAction(MeleeBufferedAction.Kick), Is.True);
            Assert.That(actor.TryContinueBufferedAction(), Is.False);
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
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(shoveContactSeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(shoveDurationSeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(shoveCost: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(shoveContactWindowSeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(kickWindupSeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(kickActiveSeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(kickHitRecoverySeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(kickMissRecoverySeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(kickObstacleRecoverySeconds: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(kickCost: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(kickDamage: value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(kickStaggerSeconds: value));
            if (value != 0f)
                Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(attackCost: value));
            else Assert.That(new MeleeCombatSettings(attackCost: 0f).AttackCost, Is.Zero, "Only the swing may be free.");
        }

        [Test]
        public void FrameTableInvariantsAreEnforcedByTheConstructor()
        {
            Assert.That(MeleeCombatSettings.Crowbar, Is.Not.Null);
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(shoveDurationSeconds: S.ShoveContactSeconds));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(shoveContactWindowSeconds: S.ShoveDurationSeconds));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(staggerSeconds: S.HitRecoverySeconds),
                "A landed hit must leave the attacker free before the victim.");
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(counterHitStaggerBonus: .05f),
                "Legacy serialized timing remains validated for compatibility.");
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(parriedRecoverySeconds: .3f),
                "A parry must guarantee one light punish.");
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(attackBufferSeconds: S.HitRecoverySeconds + .05f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(kickHitRecoverySeconds: S.AttackBufferSeconds - .01f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(parryMaxPower: 1.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(guardBreakDamageScale: 1.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeCombatSettings(chainWindupSeconds: S.WindupSeconds),
                "Legacy timing inputs still reject inconsistent values.");
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
