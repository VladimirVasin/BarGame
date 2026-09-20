using System;
using System.Collections.Generic;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>What a policy may know: only what the range's opponent can see too.</summary>
    internal readonly struct DuelView
    {
        public DuelView(CombatDuelSimulator duel, int self)
        {
            Me = duel.Actors[self];
            Enemy = duel.Actors[1 - self];
            Distance = duel.Distance;
            Clock = duel.Clock;
            EnemyTellAge = duel.TellAge(1 - self);
            EnemyContactIn = duel.ContactIn(1 - self);
            EnemyEvading = duel.IsEvading(1 - self);
        }

        public MeleeCombatant Me { get; }
        public MeleeCombatant Enemy { get; }
        public float Distance { get; }
        public float Clock { get; }
        /// <summary>Seconds since the enemy's current charge or swing began; negative when there is none.</summary>
        public float EnemyTellAge { get; }
        /// <summary>Seconds until the enemy's live swing meets you; infinite while it is only charging.</summary>
        public float EnemyContactIn { get; }
        public bool EnemyEvading { get; }
        public bool InReach => Distance <= CombatDuelSimulator.Reach;
    }

    internal interface ICombatPolicy
    {
        string Name { get; }
        void Act(DuelView view, DuelCommands commands);
    }

    /// <summary>The verbs one fighter has on the line. Movement is applied by the simulator after both acted.</summary>
    internal sealed class DuelCommands
    {
        private readonly CombatDuelSimulator duel;
        private readonly int self;
        internal DuelCommands(CombatDuelSimulator duel, int self) { this.duel = duel; this.self = self; }
        public void Approach() => duel.Move(self, -1f);
        public void Retreat() => duel.Move(self, 1f);
        public bool Attack() => duel.Actors[self].RequestAttack();
        public bool Charge() => duel.Actors[self].RequestCharge();
        public bool Release() => duel.Actors[self].ReleaseCharge();
        public void Guard(bool held) => duel.Actors[self].SetBlocking(held);
        public bool StepBack() => duel.StartStep(self, 1f, false);
        public bool StepForward() => duel.StartStep(self, -1f, false);
        public bool StepSide() => duel.StartStep(self, 0f, true);
        public float Jitter(float amplitude) => duel.Jitter(self, amplitude);
    }

    /// <summary>
    /// A one-dimensional duel on the pure rules: two <see cref="MeleeCombatant"/>s, a reach,
    /// the range's phase movement multipliers, contacts collected then applied exactly as
    /// <c>CombatTestRoot.Tick</c> does. A side step leaves the line for its travel, a back
    /// step opens the gap; nothing here is invulnerable, it is out of reach or it is not.
    /// </summary>
    internal sealed class CombatDuelSimulator
    {
        public const float Step = 1f / 120f;
        public const float Reach = 1.15f;
        public const float ContactOffset = .07f;
        public const float WalkSpeed = 2.2f;
        public readonly MeleeCombatant[] Actors = { new MeleeCombatant(), new MeleeCombatant() };
        public readonly ICombatPolicy[] Policies;
        public readonly int[] Contacts = new int[2], CounterHits = new int[2], Parries = new int[2], GuardBreaks = new int[2], Whiffs = new int[2];
        public float Distance = 1.1f;
        public float Clock { get; private set; }
        public float GuardBrokenAt = -1f, BreathEmptyAt = -1f;
        private readonly float[] move = new float[2], prevElapsed = new float[2], stepTravelLeft = new float[2], stepSign = new float[2];
        private readonly bool[] stepSideways = new bool[2], offLine = new bool[2];
        private readonly int[] prevSequence = new int[2];
        private readonly float[] tellStartedAt = { -1f, -1f };
        private readonly int[] tellSequence = { -1, -1 };
        private readonly List<(int source, float damage, float blockCost, float power, bool counter)> pending = new List<(int, float, float, float, bool)>(2);
        private uint rng;

        public CombatDuelSimulator(ICombatPolicy a, ICombatPolicy b, uint seed, float distance = 1.1f)
        {
            Policies = new[] { a, b };
            rng = seed == 0u ? 0x9e3779b9u : seed;
            Distance = distance;
        }

        public bool Finished => Actors[0].IsDefeated || Actors[1].IsDefeated;
        public int Winner => Actors[1].IsDefeated ? (Actors[0].IsDefeated ? -1 : 0) : Actors[0].IsDefeated ? 1 : -1;

        public void Run(float seconds)
        {
            float end = Clock + seconds;
            while (Clock < end - .000001f && !Finished) Tick();
        }

        public void Tick()
        {
            for (int i = 0; i < 2; i++)
            {
                move[i] = 0f;
                Policies[i].Act(new DuelView(this, i), new DuelCommands(this, i));
            }
            for (int i = 0; i < 2; i++) ApplyMovement(i);
            pending.Clear();
            for (int i = 0; i < 2; i++) AdvanceActor(i);
            foreach ((int source, float damage, float blockCost, float power, bool counter) contact in pending)
            {
                int target = 1 - contact.source;
                MeleeHitResult result = Actors[target].ReceiveHit(contact.damage, contact.blockCost, true, contact.power);
                Actors[contact.source].RecordAttackOutcome(result, Actors[contact.source].AttackSequence);
                Contacts[contact.source]++;
                if (result == MeleeHitResult.Parried) Parries[target]++;
                if (result == MeleeHitResult.GuardBroken) { GuardBreaks[contact.source]++; if (GuardBrokenAt < 0f) GuardBrokenAt = Clock; }
                if (contact.counter && (result == MeleeHitResult.Hit)) CounterHits[contact.source]++;
            }
            Clock += Step;
            for (int i = 0; i < 2; i++)
                if (BreathEmptyAt < 0f && Actors[i].Stamina <= .0001f) BreathEmptyAt = Clock;
        }

        private void ApplyMovement(int i)
        {
            MeleeCombatant me = Actors[i];
            if (stepTravelLeft[i] > 0f)
            {
                float slice = Math.Min(Step, stepTravelLeft[i]);
                stepTravelLeft[i] -= slice;
                if (!stepSideways[i]) Distance = Math.Max(.3f, Distance + stepSign[i] * me.Settings.StepDistance * slice / me.Settings.StepTravelSeconds);
                return;
            }
            if (move[i] == 0f) return;
            float scale = me.Phase switch
            {
                MeleePhase.Charging => .22f,
                MeleePhase.Windup => .35f,
                MeleePhase.Active => 0f,
                MeleePhase.Recovery => .4f,
                MeleePhase.Ready => me.IsBlocking ? .45f : 1f,
                _ => 0f
            };
            Distance = Math.Max(.3f, Distance + move[i] * WalkSpeed * scale * Step);
        }

        private void AdvanceActor(int i)
        {
            MeleeCombatant me = Actors[i];
            MeleeCombatant enemy = Actors[1 - i];
            int sequenceBefore = me.AttackSequence;
            float elapsedBefore = prevElapsed[i];
            if (me.IsCharging || me.IsAttacking)
            {
                if (tellSequence[i] != me.AttackSequence)
                {
                    tellSequence[i] = me.AttackSequence;
                    tellStartedAt[i] = Clock;
                    // A new swing re-aims at its target; whoever left the line is on it again.
                    offLine[i] = offLine[1 - i] = false;
                }
            }
            else tellSequence[i] = -1;
            bool wasAttacking = me.IsAttacking;
            me.Advance(Step);
            if (wasAttacking && me.AttackSequence == sequenceBefore && me.AttackOutcome == MeleeAttackOutcome.None || me.Phase == MeleePhase.Active)
            {
                float contactAt = me.AttackWindupSeconds + ContactOffset;
                if (me.AttackSequence == sequenceBefore && elapsedBefore < contactAt && me.AttackElapsed >= contactAt &&
                    me.Phase == MeleePhase.Active)
                {
                    bool reachable = Distance <= Reach && !IsEvading(1 - i) && !enemy.IsDefeated;
                    if (reachable && me.TryRegisterHit(1 - i, me.AttackSequence))
                    {
                        bool counter = enemy.Phase == MeleePhase.Charging || enemy.Phase == MeleePhase.Windup ||
                            (enemy.Phase == MeleePhase.Recovery &&
                             (enemy.AttackOutcome == MeleeAttackOutcome.Miss || enemy.AttackOutcome == MeleeAttackOutcome.Obstacle));
                        pending.Add((i, me.AttackDamage, me.AttackBlockCost, me.AttackPower, counter));
                    }
                }
            }
            if (me.AttackSequence == sequenceBefore && wasAttacking && me.AttackOutcome == MeleeAttackOutcome.Miss &&
                prevSequence[i] != me.AttackSequence)
            {
                Whiffs[i]++;
                prevSequence[i] = me.AttackSequence;
            }
            prevElapsed[i] = me.AttackSequence == sequenceBefore ? me.AttackElapsed : 0f;
        }

        internal void Move(int i, float sign) { move[i] = sign; if (sign != 0f) offLine[i] = false; }

        internal bool StartStep(int i, float sign, bool sideways)
        {
            MeleeCombatant me = Actors[i];
            if (!me.TryStartStep()) return false;
            stepTravelLeft[i] = me.Settings.StepTravelSeconds;
            stepSign[i] = sign;
            stepSideways[i] = sideways;
            // A side step leaves the line and stays off it until someone swings or walks again.
            offLine[i] = sideways;
            return true;
        }

        internal bool IsEvading(int i) => stepSideways[i] && (stepTravelLeft[i] > 0f || offLine[i]);
        internal float TellAge(int i) => tellSequence[i] < 0 ? -1f : Clock - tellStartedAt[i];

        internal float ContactIn(int i)
        {
            MeleeCombatant actor = Actors[i];
            if (!actor.IsAttacking) return float.PositiveInfinity;
            float contactAt = actor.AttackWindupSeconds + ContactOffset;
            return contactAt - actor.AttackElapsed;
        }

        internal float Jitter(int i, float amplitude)
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return ((rng & 1023u) / 1023f * 2f - 1f) * amplitude;
        }
    }

    /// <summary>The named ways to play the range, each a caricature of one habit.</summary>
    internal static class CombatPolicies
    {
        private const float Reaction = .2f;

        /// <summary>Holds guard forever and never swings.</summary>
        internal sealed class Turtle : ICombatPolicy
        {
            public string Name => "Turtle";
            public void Act(DuelView v, DuelCommands c) { c.Guard(true); }
        }

        /// <summary>Guards only when a tell is seen, releases soon after; never swings.</summary>
        internal sealed class TapGuard : ICombatPolicy
        {
            private float releaseAt = -1f;
            public string Name => "TapGuard";
            public void Act(DuelView v, DuelCommands c)
            {
                if (v.EnemyTellAge >= Reaction && v.EnemyTellAge < Reaction + .05f && v.InReach) releaseAt = v.Clock + .35f;
                c.Guard(v.Clock < releaseAt);
            }
        }

        /// <summary>Swings whenever it can, with a human's small hesitation; closes the distance otherwise.</summary>
        internal sealed class Spammer : ICombatPolicy
        {
            private float swingAt = -1f;
            public string Name => "Spammer";
            public void Act(DuelView v, DuelCommands c)
            {
                c.Guard(false);
                if (!v.InReach) { c.Approach(); swingAt = -1f; return; }
                if (v.Me.Phase != MeleePhase.Ready) { swingAt = -1f; return; }
                if (swingAt < 0f) swingAt = v.Clock + .04f + c.Jitter(.04f);
                if (v.Clock >= swingAt) c.Attack();
            }
        }

        /// <summary>Swings and queues the next press in every recovery tail, taking the backhand.</summary>
        internal sealed class ChainPresser : ICombatPolicy
        {
            public string Name => "ChainPresser";
            public void Act(DuelView v, DuelCommands c)
            {
                c.Guard(false);
                if (!v.InReach) { c.Approach(); return; }
                if (v.Me.Phase == MeleePhase.Ready || v.Me.ActionRemaining <= v.Me.Settings.AttackBufferSeconds) c.Attack();
            }
        }

        /// <summary>Presses guard just before each seen light contact (read + human jitter), then punishes.</summary>
        internal sealed class ParryBot : ICombatPolicy
        {
            private float pressAt = -1f, releaseAt = -1f, punishAt = -1f;
            private int readSequence = -1;
            public string Name => "ParryBot";
            public void Act(DuelView v, DuelCommands c)
            {
                if (v.Enemy.IsAttacking && v.Enemy.AttackSequence != readSequence && v.EnemyTellAge >= Reaction)
                {
                    readSequence = v.Enemy.AttackSequence;
                    float contactIn = v.EnemyContactIn;
                    pressAt = v.Clock + Math.Max(0f, contactIn - .06f + c.Jitter(.04f));
                    releaseAt = pressAt + .15f;
                }
                if (v.Me.Phase == MeleePhase.GuardImpact && v.Me.ActionRemaining <= v.Me.Settings.ParryImpactSeconds + .001f)
                    punishAt = v.Clock;
                bool hold = v.Clock >= pressAt && v.Clock < releaseAt;
                c.Guard(hold);
                if (!hold && punishAt >= 0f && v.Me.Phase == MeleePhase.Ready && v.InReach) { c.Attack(); punishAt = -1f; }
                if (!v.InReach) c.Approach();
            }
        }

        /// <summary>Taps guard on a rhythm regardless of what the enemy does (a human's jittered one),
        /// punishing any parry it stumbles into.</summary>
        internal sealed class RhythmMasher : ICombatPolicy
        {
            private float punishAt = -1f, pressAt = 0f, releaseAt = -1f;
            public string Name => "RhythmMasher";
            public void Act(DuelView v, DuelCommands c)
            {
                if (v.Clock >= pressAt && releaseAt < 0f) releaseAt = v.Clock + v.Me.Settings.ParryWindowSeconds + .05f;
                if (releaseAt >= 0f && v.Clock >= releaseAt)
                {
                    pressAt = v.Clock + v.Me.Settings.ParryRearmSeconds + .02f + c.Jitter(.06f);
                    releaseAt = -1f;
                }
                bool hold = releaseAt >= 0f;
                if (v.Me.Phase == MeleePhase.GuardImpact && v.Me.ActionRemaining <= v.Me.Settings.ParryImpactSeconds + .001f)
                    punishAt = v.Clock;
                c.Guard(hold);
                if (!hold && punishAt >= 0f && v.Me.Phase == MeleePhase.Ready && v.InReach) { c.Attack(); punishAt = -1f; }
                if (!v.InReach) c.Approach();
            }
        }

        /// <summary>Winds up once and never lets go.</summary>
        internal sealed class ChargeHolder : ICombatPolicy
        {
            public string Name => "ChargeHolder";
            public void Act(DuelView v, DuelCommands c) { if (!v.Me.IsCharging) c.Charge(); }
        }

        /// <summary>Steps off the line on every seen tell and strikes straight out of the step.</summary>
        internal sealed class StepBot : ICombatPolicy
        {
            private int readSequence = -1;
            public string Name => "StepBot";
            public void Act(DuelView v, DuelCommands c)
            {
                c.Guard(false);
                if (v.Enemy.IsAttacking && v.Enemy.AttackSequence != readSequence && v.EnemyTellAge >= Reaction && v.InReach)
                {
                    readSequence = v.Enemy.AttackSequence;
                    if (v.Me.Phase == MeleePhase.Ready && c.StepSide()) return;
                }
                if (v.Me.Phase == MeleePhase.Step && v.Me.ActionRemaining <= v.Me.Settings.AttackBufferSeconds) { c.Attack(); return; }
                if (!v.InReach) c.Approach();
                else if (v.Me.Phase == MeleePhase.Ready && v.Enemy.Phase == MeleePhase.Recovery) c.Attack();
            }
        }

        /// <summary>Holds guard, swings right after every block, then guards again.</summary>
        internal sealed class BlockCounter : ICombatPolicy
        {
            private bool counter;
            public string Name => "BlockCounter";
            public void Act(DuelView v, DuelCommands c)
            {
                if (v.Me.Phase == MeleePhase.GuardImpact) counter = true;
                if (counter && v.Me.Phase == MeleePhase.Ready && v.InReach) { c.Guard(false); c.Attack(); counter = false; return; }
                c.Guard(!v.Me.IsAttacking);
                if (!v.InReach) c.Approach();
            }
        }

        /// <summary>Never guards, always trades.</summary>
        internal sealed class Trader : ICombatPolicy
        {
            public string Name => "Trader";
            public void Act(DuelView v, DuelCommands c)
            {
                c.Guard(false);
                if (!v.InReach) { c.Approach(); return; }
                if (v.Me.Phase == MeleePhase.Ready) c.Attack();
            }
        }
    }
}
