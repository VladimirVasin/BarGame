using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        public const float ZombieFrenzyDurationSeconds = 5f;
        private const float ZombieFrenzySinkSeconds = .35f;
        private AnimationClip zombieFrenzy;
        private Transform zombieFrenzyPelvis;
        private float zombieFrenzyEntryHeight, zombieFrenzyKneelHeight, zombieFrenzyEntryTime;
        private bool zombieFrenzied, zombieFrenzySequenceValid;
        private int zombieFrenzySequence;
        public bool IsZombieFrenzied => IsZombie && zombieFrenzied;
        public float ZombieFrenzySecondsElapsed { get; private set; }

        private void InitializeZombieFrenzyEntry()
        {
            zombieFrenzyPelvis = CityPedestrianHandProps.FindSocket(DamageRigRoot, "pelvis");
            if (zombieFrenzyPelvis == null) throw new System.InvalidOperationException("Porter frenzy requires its retained pelvis.");
            SampleNpcClip(zombieFrenzy, 0f);
            zombieFrenzyEntryHeight = transform.InverseTransformPoint(zombieFrenzyPelvis.position).y;
            SampleNpcClip(zombieFrenzy, ZombieFrenzySinkSeconds);
            zombieFrenzyKneelHeight = transform.InverseTransformPoint(zombieFrenzyPelvis.position).y;
            SampleNpcClip(ready, 0f);
        }

        private float ZombieFrenzySampleTime
        {
            get
            {
                float clock = ZombieFrenzySecondsElapsed;
                return clock < ZombieFrenzySinkSeconds
                    ? Mathf.Lerp(zombieFrenzyEntryTime, ZombieFrenzySinkSeconds, clock / ZombieFrenzySinkSeconds) : clock;
            }
        }

        // Called only after the victim has actually entered its physical fall.
        // A received impulse can recover its balance instead, or topple later.
        private void ReactToZombieKnockdown(CombatImpact impact)
        {
            if (!(IsDocker || IsCrane) || !ZombieAwake || impact.Source != this || impact.Target == null || !impact.Target.IsHero ||
                impact.Kind != CombatImpactKind.Unarmed || impact.AttackSequence != State.AttackSequence ||
                !impact.Target.IsKnockedDown || !IsAvailable || roundEnded || State.IsDefeated || State.IsStunned ||
                IsKnockedDown || IsRagdollActive || !BodyDamage.CanStand || zombieFrenzied ||
                zombieFrenzySequenceValid && zombieFrenzySequence == impact.AttackSequence) return;
            zombieFrenzySequenceValid = true;
            zombieFrenzySequence = impact.AttackSequence;
            if (IsCrane)
            {
                // The suspended worker has neither the porter's loaded pelvis
                // nor its planted kneel. Its retained crane rig owns the reaction.
                zombieFrenzied = true;
                ZombieFrenzySecondsElapsed = 0f;
                BeginCraneFrenzy(impact.AttackSequence);
                JournalEvent("zombie_frenzy_started", impact.Target.JournalActorId, impact.AttackSequence);
                return;
            }
            // A delayed loss of balance may find the attacker already kneeling.
            // Enter at that depth, keeping the cue's later impulse times intact.
            float depth = Mathf.InverseLerp(zombieFrenzyEntryHeight, zombieFrenzyKneelHeight,
                transform.InverseTransformPoint(zombieFrenzyPelvis.position).y);
            float low = 0f, high = 1f;
            for (int i = 0; i < 12; i++)
            {
                float u = (low + high) * .5f;
                float eased = u * u * u * (10f - 15f * u + 6f * u * u);
                if (eased < depth) low = u; else high = u;
            }
            zombieFrenzyEntryTime = depth <= 0f ? 0f : depth >= 1f ? ZombieFrenzySinkSeconds :
                (low + high) * .5f * ZombieFrenzySinkSeconds;
            zombieFrenzied = true;
            ZombieFrenzySecondsElapsed = 0f;
            zombieWalkingRequested = false;
            locomotionVelocity = Vector3.zero;
            zombieLoad?.EndRam();
            zombieContactPoseValid = collectSweep = sweepValid = false;
            reaction = null;
            zombieAudio?.Frenzy();
            JournalEvent("zombie_frenzy_started", impact.Target.JournalActorId, impact.AttackSequence);
        }

        private void InterruptZombieFrenzyIfNeeded()
        {
            if (IsZombieFrenzied && (roundEnded || State.IsDefeated || State.IsStunned || IsKnockedDown ||
                IsRagdollActive || !BodyDamage.CanStand || !IsAvailable)) EndZombieFrenzy();
        }

        private void AdvanceZombieFrenzy(float seconds)
        {
            if (presentationFrozen) return;
            ZombieFrenzySecondsElapsed = Mathf.Min(ZombieFrenzyDurationSeconds, ZombieFrenzySecondsElapsed + seconds);
            if (ZombieFrenzySecondsElapsed + .000001f < ZombieFrenzyDurationSeconds) return;
            EndZombieFrenzy();
            // The authored terminal pose equals ready/0. Do not blend back to
            // an old kneeling recovery or resume the pre-attack walk phase.
            poseClock = zombieWorkClock = zombieWalkClock = 0f;
            visibleClip = null;
            if (!IsCrane) CancelPoseBlend();
        }

        private void EndZombieFrenzy()
        {
            if (!zombieFrenzied) return;
            zombieFrenzied = false;
            zombieAudio?.EndFrenzy();
            if (IsCrane) EndCraneFrenzy();
        }

        private void ResetZombieFrenzy()
        {
            EndZombieFrenzy();
            zombieFrenzySequenceValid = false;
            zombieFrenzySequence = 0;
            zombieFrenzyEntryTime = 0f;
            ZombieFrenzySecondsElapsed = 0f;
        }
    }
}
