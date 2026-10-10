using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Scene-local adapter: one rules clock drives both visible motion and weapon contact.</summary>
    public sealed partial class CombatActor : MonoBehaviour
    {
        private Player3DCharacterPresentation hero;
        private PlayerMotor motor;
        private PlayerAnimatedInteractionController interaction;
        private VillageResidentPresentation npc;
        private NpcHandPose handPose;
        private AnimationClip ready, rest, block, hit, guardImpact, guardBreak, reaction, defeat;
        private CombatSupportGrip supportGrip;
        private CombatWeaponConstraint weaponConstraint;
        public bool WeaponClearanceBlocked => weaponConstraint != null && weaponConstraint.Blocked;
        public float WeaponPenetrationDepth => weaponConstraint?.PenetrationDepth ?? 0f;
        public string WeaponBlockingShape => weaponConstraint?.BlockingShape;
        private Transform strikeBase, strikeTip;
        private string visibleClip;
        private int visibleAttackSequence;
        private bool presentationFrozen;
        private bool simulationPosePending;
        private float poseClock, reactionClock;
        internal long NpcAnimationSampleTicks { get; private set; }
        internal long NpcPoseCompositionTicks { get; private set; }
        internal long NpcPoseSnapshotTicks { get; private set; }
        private bool receivedDuringStep;
        private bool guardHeld;
        private readonly List<Contact> standaloneContacts = new List<Contact>(4);
        public MeleeCombatant State { get; } = new MeleeCombatant();
        public CharacterController Body { get; private set; }
        public GameObject Weapon { get; private set; }
        public CombatWeaponId WeaponId { get; private set; }
        public bool IsPistol => WeaponId == CombatWeaponId.Pistol;
        public bool IsShotgun => WeaponId == CombatWeaponId.Shotgun;
        public bool IsFirearm => IsPistol || IsShotgun;
        public bool IsHero => hero != null;
        public Vector3 SupportGripWorldPosition => supportGrip != null ? supportGrip.Target : transform.position;
        public float SupportGripWeight => supportGrip?.Weight ?? 0f;
        public CombatArmSupportState SupportArmState => supportGrip?.State ?? CombatArmSupportState.SupportingWeapon;
        public bool IsAvailable => isActiveAndEnabled && (IsBodyGrounded || hero == null ||
            (hero.CanAcquireClip(this) && !interaction.IsActive && motor.InputEnabled));

        // Finishing a round prevents further attacks, but the standing winner
        // still walks. Defeat/stagger and the active swing own their own stop.
        public float MovementScale => !BodyDamage.CanStand || IsKnockedDown || (footwork?.RecoveryEpisodeActive ?? false) ||
            (ImpactMotion != null && ImpactMotion.Velocity.sqrMagnitude > .04f) ? 0f : State.Phase switch
        {
            MeleePhase.Charging => .22f,
            // The swing gathers itself instead of snapping from a walk to a halt.
            MeleePhase.Windup => Mathf.Lerp(.55f, .2f, State.PhaseProgress),
            MeleePhase.Active => 0f,
            MeleePhase.Recovery => Mathf.Lerp(.15f, .65f, State.PhaseProgress),
            MeleePhase.Ready => IsFirearm && Firearm != null && Firearm.IsReloading ? .55f : State.IsBlocking ? .45f : 1f,
            MeleePhase.Kicking => 0f,
            _ => 0f
        };

        // Before sole support is refreshed, an ordinary turn can raise the
        // measured load. Only a live impact owns balance-related turn blocking.
        internal float TurnScale => IsKnockedDown || (footwork?.RecoveryEpisodeActive ?? false) ||
            (ImpactMotion != null && ImpactMotion.IsActive && ImpactMotion.BalanceLoad > .45f) ? 0f : State.Phase switch
        {
            MeleePhase.Charging => .2f,
            MeleePhase.Windup => .2f,
            MeleePhase.Active => 0f,
            MeleePhase.Recovery => Mathf.Lerp(.15f, .75f, State.PhaseProgress),
            MeleePhase.Ready => 1f,
            MeleePhase.Kicking => 0f,
            // A rocked body turns at a third of the free rate. Planted steps and
            // block impacts hold; attack facing spends its separate supported budget.
            MeleePhase.Stagger or MeleePhase.GuardBroken => .35f,
            _ => 0f
        };

        public void InitializeHero(PlayerRuntime player, CombatWeaponId weapon = CombatWeaponId.Crowbar)
        {
            WeaponId = weapon;
            hero = (Player3DCharacterPresentation)player.Visual;
            motor = player.Motor;
            interaction = GetComponent<PlayerAnimatedInteractionController>();
            Body = GetComponent<CharacterController>();
            LoadClips(false);
            void Register(AnimationClip clip) => hero.Registry.RegisterRuntimeAnimation(new Player3DAnimationBinding(
                clip.name, "Combat", clip, clip.length, clip.isLooping));
            foreach (AnimationClip clip in new[] { ready, rest, block, hit, guardImpact, guardBreak, defeat }) Register(clip);
            foreach (SwingClips side in swings)
                foreach (AnimationClip clip in new[] { side.Attack, side.ReleaseLight, side.ReleaseHeavy, side.Charge, side.Recoil }) Register(clip);
            foreach (string name in CombatAssetProvider.LocomotionClipNames)
            {
                AnimationClip clip = CombatAssetProvider.LoadClip(name);
                hero.Registry.RegisterRuntimeAnimation(new Player3DAnimationBinding(
                    clip.name, "Combat", clip, clip.length, clip.isLooping));
            }
            LoadStepClips(false);
            Ragdoll = gameObject.AddComponent<CombatRagdoll>();
            Ragdoll.InitializeHero(player);
            AttachWeapon(hero.Registry.Anchors.RightGrip);
            if (IsFirearm) InitializePistol();
            hero.RegisterAccessoryRenderers(Weapon.GetComponentsInChildren<Renderer>());
            InitializeDamagePose();
            LoadKickClip(false);
            InitializeCombatAttention();
            Present();
        }

        public void InitializeOpponent(VillageResidentPresentation presentation, CharacterController body)
        {
            npc = presentation;
            Body = body;
            npc.ReleaseAnimation();
            LoadClips(true);
            InitializeNpcAnimation();
            InitializeNpcPoseBlend();
            InitializeNpcChargeBlend();
            LoadStepClips(true);
            Ragdoll = gameObject.AddComponent<CombatRagdoll>();
            Ragdoll.InitializeOpponent(presentation, body);
            AttachWeapon(npc.RightGrip);
            InitializeDamagePose();
            InitializeCombatAttention();
            Present();
        }

        private void LoadClips(bool forNpc)
        {
            ready = CombatAssetProvider.LoadClip("CombatReady", forNpc);
            rest = CombatAssetProvider.LoadClip("CombatRest", forNpc);
            block = CombatAssetProvider.LoadClip("CombatBlock", forNpc);
            hit = CombatAssetProvider.LoadClip("CombatHit", forNpc);
            guardImpact = CombatAssetProvider.LoadClip("CombatGuardImpact", forNpc);
            guardBreak = CombatAssetProvider.LoadClip("CombatGuardBreak", forNpc);
            defeat = CombatAssetProvider.LoadClip("CombatDefeat", forNpc);
            LoadSwingClips(forNpc);
        }

        private void AttachWeapon(Transform grip)
        {
            handPose = grip.GetComponentInParent<NpcHandPose>();
            if (IsFirearm)
            {
                Weapon = IsShotgun ? CombatShotgunAssetProvider.CreateShotgun(grip, handPose) :
                    CombatPistolAssetProvider.CreatePistol(grip, handPose);
                PrepareWeaponPhysics();
                return;
            }
            Weapon = CombatAssetProvider.CreateCrowbar(grip, handPose);
            strikeBase = CombatAssetProvider.FindAnchor(Weapon, "StrikeBase");
            strikeTip = CombatAssetProvider.FindAnchor(Weapon, "StrikeTip");
            if (strikeBase == null || strikeTip == null) throw new InvalidOperationException("Crowbar needs its authored strike anchors.");
            PrepareWeaponPhysics();
            supportGrip = new CombatSupportGrip(DamageRigRoot, transform, handPose, Weapon.transform);
            supportGrip.JournalActor = this;
            weaponConstraint = new CombatWeaponConstraint(this);
            supportGrip.SetArmClearance(weaponConstraint.SupportArmClearance);
            if (hero != null) hero.SetCombatSupportGrip(this, supportGrip, weaponConstraint);
        }

        // Guard and kicks need stable support. An upper-body attempt may share
        // a catching step, but can never cancel an already committed fall.
        internal bool HasAttackBalance => BodyDamage.CanStand && !IsKnockedDown && !(ImpactMotion?.RecoveryInProgress ?? false) &&
            !(footwork?.RecoveryEpisodeActive ?? false);
        private bool CanAttemptBodyAction => BodyDamage.CanStand && !IsKnockedDown && !State.IsKnockedDown &&
            !IsRagdollActive && !State.IsDefeated && !(ImpactMotion?.WantsKnockdown ?? false);
        internal bool CanAttemptUpperBodyAttack => BodyDamage.CanUseRightHand && !weaponDropped && CanAttemptBodyAction;
        private string UpperBodyAttackRejection => !BodyDamage.CanUseRightHand ? "right_hand_unavailable" : weaponDropped ? "weapon_missing" : IsKnockedDown || State.IsKnockedDown || IsRagdollActive
            ? "knocked_down" : State.IsDefeated ? "defeated" : "fall_committed";
        internal const float WeaponSpacing = 1f;
        internal void ApplyMotorConstraint()
        {
            if (motor == null) return;
            if (!NeedsCombatPresentation) { motor.ReleaseMovementConstraint(this); return; }
            motor.SetOwnedMovementConstraint(this, MovementScale,
                OwnsCombatFacing || (!CombatFocused && CommittedActionOwnsFacing) ? 0f : TurnScale,
                contactTarget != null ? contactTarget.transform : null,
                CombatFocused && State.Phase == MeleePhase.Windup ? WeaponSpacing : 0f,
                firearmWalking: UsesFirearmWalkLocomotion,
                sprintBlocked: IsFirearm && (Firearm.AimRequested || pistolVisualAimProgress > 0f));
        }
        private string AttackBalanceRejection => IsKnockedDown ? "knocked_down" : "balance_recovery";
        internal bool HasTwoHandSupport => BodyDamage.CanUseLeftHand && BodyDamage.CanUseRightHand && !weaponDropped && HasAttackBalance &&
            (supportGrip == null || supportGrip.IsSupportingWeapon);
        public bool GuardRequested => guardHeld;
        public bool GuardReady => GuardSupportRejection == null &&
            (State.Phase == MeleePhase.Ready || State.Phase == MeleePhase.GuardImpact);
        internal string GuardSupportRejection => IsFirearm ? "firearm" : !CombatFocused ? "unfocused" : weaponDropped ? "weapon_missing" : roundEnded ? "round_ended" : !IsAvailable ? "actor_unavailable" :
            State.IsDefeated ? "defeated" : !HasAttackBalance ? AttackBalanceRejection :
            !HasTwoHandSupport ? "two_hand_support" : null;
        internal CombatFootwork Footwork => footwork;
        internal Vector3 AchievedPlanarVelocity => motor != null ? motor.PlanarVelocity : locomotionVelocity;
        internal CombatSupportGrip SupportGrip => supportGrip;

        public bool TryAttack()
        {
            if (IsFirearm) return false;
            int request = JournalCommand("attack_immediate");
            if (!CombatFocused && !CanAttackFinishedBody) return JournalCommandResult(request, "rejected", "unfocused");
            if (roundEnded && !CanAttackFinishedBody) return JournalCommandResult(request, "rejected", "round_ended");
            if (!IsAvailable) return JournalCommandResult(request, "rejected", "actor_unavailable");
            if (!GameInput.CanRead(GameInputContext.Gameplay)) return JournalCommandResult(request, "rejected", "input_gate");
            if (BodyDamage.CanUseLeftHand && CheckShoveRange(request)) return TryBeginShove(request);
            if (!CanAttemptUpperBodyAttack) return JournalCommandResult(request, "rejected", UpperBodyAttackRejection);
            if (State.Phase == MeleePhase.Step && State.CanTransitionTo(MeleeBufferedAction.Attack, true) &&
                !(footwork?.PrepareStepAttackHandoff() ?? false))
                return JournalCommandResult(request, "rejected", "step_support_missing");
            if (!State.TryStartRecoveryAttack()) return JournalRulesRejected(request, State.Settings.AttackCost);
            CancelPendingKick("replaced");
            reaction = null;
            ResumeFinishedBodyAttack();
            Present();
            return JournalCommandResult(request, "started", "attack");
        }

        public bool RequestAttack()
        {
            if (IsFirearm) return false;
            int request = JournalCommand("attack");
            if (!CombatFocused && !CanAttackFinishedBody) return JournalCommandResult(request, "rejected", "unfocused");
            if (roundEnded && !CanAttackFinishedBody) return JournalCommandResult(request, "rejected", "round_ended");
            if (!IsAvailable) return JournalCommandResult(request, "rejected", "actor_unavailable");
            if (!GameInput.CanRead(GameInputContext.Gameplay)) return JournalCommandResult(request, "rejected", "input_gate");
            if (!State.IsAttacking && BodyDamage.CanUseLeftHand && CheckShoveRange(request)) return TryBeginShove(request);
            if (!CanAttemptUpperBodyAttack) return JournalCommandResult(request, "rejected", UpperBodyAttackRejection);
            int previous = State.AttackSequence;
            if (!State.RequestAttack(true)) return JournalRulesRejected(request, State.Settings.AttackCost);
            CancelPendingKick("replaced");
            ContinueBufferedAttackAfterContacts();
            if (previous != State.AttackSequence) reaction = null;
            ResumeFinishedBodyAttack();
            Present();
            return JournalCommandResult(request, previous == State.AttackSequence ? "queued" : "started", "attack");
        }

        internal bool TryObservedCounterAttack()
        {
            int request = JournalCommand("observed_counter");
            if (!CombatFocused || roundEnded || !IsAvailable || !CanAttemptUpperBodyAttack ||
                !GameInput.CanRead(GameInputContext.Gameplay))
                return JournalCommandResult(request, "rejected", "counter_unavailable");
            if (BodyDamage.CanUseLeftHand && CheckShoveRange(request)) return TryBeginShove(request);
            if (!State.TryStartObservedCounterAttack()) return JournalRulesRejected(request, State.Settings.AttackCost);
            reaction = null;
            Present();
            return JournalCommandResult(request, "started", "observed_counter");
        }

        public void SetBlock(bool held)
        {
            if (IsFirearm) held = false;
            bool freshPress = held && !guardHeld;
            guardHeld = held && CombatFocused;
            RefreshBlock(freshPress);
        }

        private void RefreshBlock(bool freshPress = false)
        {
            string reason = !guardHeld ? "released" : GuardSupportRejection ?? "guard";
            bool allowed = reason == "guard";
            bool changed = guardHeld != journalBlockHeld || allowed != journalBlockAllowed || reason != journalBlockReason;
            int request = changed ? JournalCommand("block") : 0;
            // Restoring support while RMB is still held raises the guard at the
            // next duel step. It does not manufacture a fresh parry press.
            State.SetBlocking(allowed, freshPress);
            if (changed)
            {
                string result = !guardHeld ? "released" : !allowed || State.IsDefeated || State.IsKnockedDown ? "rejected" :
                    State.IsBlocking ? "started" : "queued";
                JournalCommandResult(request, result, allowed && (State.IsDefeated || State.IsKnockedDown)
                    ? State.IsDefeated ? "defeated" : "knocked_down" : reason, trackAction: false);
                journalBlockHeld = guardHeld; journalBlockAllowed = allowed; journalBlockReason = reason;
            }
        }

        public void Step(float seconds)
        {
            if (!GameInput.CanRead(GameInputContext.Gameplay)) return;
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            // Isolated actor stepping is retained for authoring/tests. Live duels use
            // the root's shared clock and collect BOTH actors before resolving damage.
            double remaining = seconds;
            while (remaining > .0000001d)
            {
                float step = (float)Math.Min(CombatTestRoot.SimulationStep, remaining);
                standaloneContacts.Clear();
                standaloneShoves.Clear();
                standaloneKicks.Clear();
                AdvanceSimulation(step);
                Present();
                CaptureContactPose();
                contactTarget?.CaptureContactPose();
                CollectContacts(standaloneContacts);
                CollectShoveContacts(standaloneShoves);
                CollectKickContacts(standaloneKicks);
                ApplyContacts(standaloneContacts);
                foreach (ShoveContact contact in standaloneShoves) contact.Apply();
                foreach (KickContact contact in standaloneKicks) contact.Apply();
                ContinueBufferedAttackAfterContacts();
                remaining -= step;
            }
            Present();
        }

        internal void AdvanceSimulation(float seconds, bool deferPoseMotion = false)
        {
            AdvancePistol(seconds);
            simulationPosePending = false;
            collectSweep = false;
            collectShove = false;
            collectKick = false;
            CancelInterruptedShoveContact();
            if (AdvanceKnockdown(seconds)) { CancelPendingKick("knocked_down"); return; }
            if (State.IsDefeated) { AdvanceDefeat(seconds); return; }
            if (!IsAvailable)
            {
                State.CancelAction(); reaction = null; sweepValid = false;
                CancelInterruptedShoveContact();
                ReleasePresentation(); return;
            }
            AdvanceCombatAttention(seconds);
            AdvanceVisualClock(seconds);
            AdvanceImpactMotion(seconds);
            if (guardHeld) RefreshBlock();
            if (CombatFocused && BodyDamage.CanUseLeftHand && !weaponDropped && State.Phase == MeleePhase.Windup && InShoveRange && !TryBeginShove()) State.CancelAction();
            int sequence = State.AttackSequence;
            float from = State.AttackElapsed;
            MeleePhase previousPhase = State.Phase;
            float previousStep = State.StepTravelProgress;
            // All queued actions cross their boundary after both fighters'
            // contacts and this tick's final step displacement have resolved.
            MeleeAdvanceResult elapsed = State.Advance(seconds, false);
            if (elapsed.IsKick && elapsed.HasActiveWindow)
            {
                collectKick = true; kickFrom = elapsed.ActiveStartNormalized; kickTo = elapsed.ActiveEndNormalized;
                kickSequence = elapsed.AttackSequence;
            }
            if (sequence != State.AttackSequence && journalQueuedRequest != 0)
                JournalBufferedActionStarted();
            CancelInterruptedShoveContact();
            collectShove = shoveContactPending && State.ShoveElapsed >= State.Settings.ShoveContactSeconds;
            // A queued step begins inside this advance; give it its clip and its first travel.
            if (State.Phase == MeleePhase.Step && (previousPhase != MeleePhase.Step || sequence != State.AttackSequence))
            {
                BeginStepPresentation();
                AdvanceStepMovement(0f, State.StepTravelProgress);
            }
            else if (previousPhase == MeleePhase.Step) AdvanceStepMovement(previousStep, State.StepTravelProgress);
            if (previousPhase == MeleePhase.GuardImpact && State.Phase != MeleePhase.GuardImpact && reaction == guardImpact)
                reaction = null;
            if (sequence != State.AttackSequence) { from = 0f; reaction = null; }
            // The crowbar only starts moving when the arc opens: that is where it whistles.
            if (previousPhase == MeleePhase.Windup && State.Phase != MeleePhase.Windup && State.IsAttacking &&
                sequence == State.AttackSequence)
                RetroAudio.PlayAt(RetroSfxId.SpadeToss, strikeTip.position, .35f);
            if ((State.IsAttacking && !IsRecoil(reaction)) || (elapsed.HasActiveWindow && !elapsed.IsKick))
            {
                collectSweep = true;
                sweepFrom = from;
                sweepTo = State.AttackElapsed;
                pendingSequence = State.AttackSequence;
            }
            else sweepValid = false;
            simulationPosePending = true;
            if (!deferPoseMotion) CompleteSimulationPose(seconds);
        }

        // Both actors first finish their actual step/push displacement. Facing and
        // planted feet then read those positions before either contact pose is frozen.
        internal void CompleteSimulationPose(float seconds)
        {
            if (!simulationPosePending) return;
            simulationPosePending = false;
            if (!IsAvailable || IsKnockedDown || State.IsDefeated) return;
            if (!NeedsCombatPresentation) { ReleaseFreeLocomotion(); return; }
            AdvanceCombatFacing(seconds);
            UpdateAttackReach(true);
            footwork?.SetFirearmWalkLocomotion(UsesFirearmWalkLocomotion);
            footwork?.Advance(seconds, State);
            CompleteImpactRecoveryStep(seconds);
            AdvancePendingKick(seconds);
        }

        private MeleeHitResult Receive(CombatActor source, bool front, int sequence, Vector3 point, Vector3 normal, Vector3 direction,
            float damage, float blockCost, float power, MeleeHitLocation location,
            Player3DAnatomicalPart part = Player3DAnatomicalPart.Torso, Vector3 localPoint = default, float weaponSpeed = 0f,
            bool physicalBody = false, BodyDamageRegion? bodyRegion = null, int bodyPatch = -1, bool detachedPart = false)
        {
            if (damage <= 0f) return MeleeHitResult.Ignored;
            MeleePhase phaseBefore = State.Phase;
            bool postmortem = State.IsDefeated;
            receivedDuringStep = State.Phase == MeleePhase.Step;
            float healthBefore = State.Health;
            BodyDamageRegion region = bodyRegion ?? CombatBodyAnatomy.ToRegion(part);
            float requested = MeleeDamageProfile.Crowbar.ResolveDamage(damage, State.Settings.MaxHealth, location);
            bool structuralOnly = postmortem || detachedPart;
            // Real body sweeps passed the physical weapon guard already. Charge
            // only the shared limb wound budget that can actually reach health;
            // the crowbar rules still own their anatomical damage profile.
            if (!structuralOnly && (physicalBody || !front || !State.IsBlocking))
            {
                float allowed = BodyDamage.ResolveHealthDamage(region, requested);
                structuralOnly = allowed <= 0f;
                if (requested > 0f) damage *= allowed / requested;
            }
            // Actual sweeps already tested the blocking prop before this body.
            // A guard flag cannot intercept a blade which physically went around it.
            MeleeHitResult result = structuralOnly ? MeleeHitResult.Hit :
                State.ReceiveHit(damage, blockCost, front && !physicalBody, power, location);
            if (!postmortem && !detachedPart) CancelInterruptedShoveContact();
            if (result == MeleeHitResult.Ignored) { receivedDuringStep = false; return result; }
            // Weight lives in time and motion: the body is the loudest cue, a block
            // moves both fighters, a parry throws the attacker's weapon wide.
            switch (result)
            {
                case MeleeHitResult.Hit:
                    RetroAudio.PlayAt(RetroSfxId.SpadeBite, point, Mathf.Lerp(.8f, 1f, power));
                    if (!postmortem && !detachedPart) reaction = null;
                    break;
                case MeleeHitResult.GuardBroken:
                    RetroAudio.PlayAt(RetroSfxId.SpadeBite, point, 1f);
                    RetroAudio.PlayAt(RetroSfxId.StoneTamp, transform.position + Vector3.up, .6f);
                    reaction = null;
                    break;
                case MeleeHitResult.Blocked:
                    RetroAudio.PlayAt(RetroSfxId.SpadeGlance, point, .55f);
                    source.Shove(-direction, .04f, .12f);
                    reaction = guardImpact;
                    break;
                case MeleeHitResult.Parried:
                    RetroAudio.PlayAt(RetroSfxId.SpadeGlance, point, 1f);
                    RetroAudio.PlayAt(RetroSfxId.StoneTamp, point, .9f);
                    reaction = guardImpact;
                    break;
            }
            if (!postmortem && !detachedPart) reactionClock = 0f;
            var impact = new CombatImpact(source, this, sequence, point, normal, direction,
                healthBefore, State.Health, result, location, power, part, localPoint, weaponSpeed,
                CombatImpactMotion.ResolveImpulse(direction, power, weaponSpeed, result),
                woundDamage: requested, bodyRegion: bodyRegion, bodyPatch: bodyPatch, detachedPart: detachedPart);
            // A strong fatal torso strike hands the live pose to physics before
            // publishing its momentum, just like a fatal projectile contact.
            bool immediateFall = !postmortem && !detachedPart && State.IsDefeated &&
                CombatBodyDamageState.IsTorso(region) && impact.Impulse.sqrMagnitude >= 120f * 120f;
            if (immediateFall) BeginProjectileDefeat(impact);
            PublishImpact(impact, phaseBefore);
            if (!postmortem && !detachedPart && State.IsDefeated && !immediateFall) BeginDefeat(direction, point);
            receivedDuringStep = false;
            if (!postmortem && !detachedPart) Present();
            return result;
        }

        /// <summary>The parried swing stops where it was met: the recoil clip and a short shove back.</summary>
        internal void ShowParried(Vector3 point, Vector3 direction)
        {
            reaction = Current.Recoil;
            reactionClock = 0f;
            sweepValid = false;
            Shove(-transform.forward, .10f, .14f);
        }

        /// <summary>One bounded planar shove through the motor or the opponent's controller.</summary>
        internal void Shove(Vector3 direction, float distance, float duration)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < .0001f || distance <= 0f || duration <= 0f) return;
            direction.Normalize();
            ImpactMotion?.Push(direction * (distance * 4.2f * CombatImpactMotion.BodyMass));
        }

        private bool previewWorldBlocked;

        private bool SampleAttack(float progress)
        {
            RestoreCombatAttention();
            RefreshCombatAttention();
            previewWorldBlocked = false;
            using var weaponPreview = weaponConstraint.BeginContactPreview();
            supportGrip?.Restore();
            weaponConstraint?.Restore();
            footwork?.Restore();
            damagePose?.Restore();
            bodyMotion?.Restore();
            AnimationClip chosen = ReleaseClip;
            ConfigureAttackReachPose(MeleeBufferedAction.Attack, progress);
            supportGrip?.SetTarget(false, BodyDamage.CanUseLeftHand, State.IsContinuation);
            if (hero != null)
            {
                if (visibleClip != chosen.name || !hero.OwnsClip(this))
                {
                    bool releasingCharge = visibleClip == Current.Charge.name;
                    if (!hero.TryAcquireClip(this, chosen.name, releasingCharge)) return false;
                    if (!releasingCharge) BeginPoseBlend();
                }
                visibleClip = chosen.name;
                hero.SetOwnedClipLocomotion(this, false);
                SampleHeroRelease(progress);
                hero.SetCombatBodyMotion(this, bodyMotion);
                hero.SetCombatFootwork(this, footwork);
                PresentDamagePose();
                // Contacts see the same complete pose as LateUpdate, including
                // the externally clocked transition and the supporting palm.
                hero.ReapplyLatePresentationPose();
            }
            else
            {
                if (visibleClip != chosen.name)
                {
                    if (visibleClip != Current.Charge.name) BeginPoseBlend();
                    visibleClip = chosen.name;
                }
                SampleNpcAction(chosen, progress);
                ApplyNpcCombatPose();
            }
            handPose.SetGrip(false, 1f);
            // Read before the preview restores persistent presentation diagnostics.
            // Contacts still need the world obstruction that corrected this sample.
            previewWorldBlocked = weaponConstraint.WorldBlocked;
            previewWorldFraction = weaponConstraint.WorldContactFraction;
            previewWorldPoint = weaponConstraint.WorldContactPoint; previewWorldNormal = weaponConstraint.WorldContactNormal;
            return true;
        }

        public void SetLocomotion(float speed) => SetLocomotion(transform.forward * speed);

        internal void SetPresentationFrozen(bool frozen)
        {
            presentationFrozen = frozen;
            motor?.SetOwnedMovementFrozen(this, frozen);
            if (hero != null) hero.SetOwnedPresentationFrozen(this, frozen && !IsKnockedDown && !IsRagdollActive);
            SetKnockdownFrozen(frozen);
        }

        public void SetLocomotion(Vector3 velocity)
        {
            velocity.y = 0f;
            locomotionVelocity = velocity;
        }

        public void Present()
        {
            using var journalTiming = MeasureJournalWork(JournalWork.Present);
            // The contact has already left the complete visible pose on this rig.
            // Re-solving against a newly injured target during hit-stop would move
            // an NPC's weapon off that contact at the very same animation instant.
            if (presentationFrozen || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused) return;
            if (!IsRagdollActive) RestorePistolAimPose();
            RestoreCombatAttention();
            if (ready == null || winnerPresentationReleased) return;
            if (!NeedsCombatPresentation) { ReleaseFreeLocomotion(); return; }
            if (freeLocomotionReleased && IsFirearm && hero != null &&
                hero.TryGetPresentedBonePose(hero.Registry.Anchors.LeftFoot, out Pose leftSole) &&
                hero.TryGetPresentedBonePose(hero.Registry.Anchors.RightFoot, out Pose rightSole))
                footwork?.AdoptPresentedStance(leftSole, rightSole);
            freeLocomotionReleased = false;
            BindGroundedFirearm();
            if (PresentKnockdown() || IsRagdollActive) return;
            if (!State.IsKicking) footwork?.EndKickSupport();
            RefreshCombatAttention();
            UpdateAttackReach(false);
            supportGrip?.Restore();
            weaponConstraint?.Restore();
            footwork?.SetFirearmWalkLocomotion(UsesFirearmWalkLocomotion);
            footwork?.Restore();
            damagePose?.Restore();
            bodyMotion?.Restore();
            // A shove's rules stagger is not an injury clip. If real contact
            // still supports the weapon, that clip must not release the hand.
            // Physical release, lost contact and recovery keep their own gates.
            bool supportedShove = State.Phase == MeleePhase.Stagger && LastImpact.Result == MeleeHitResult.Hit &&
                LastImpact.Damage <= 0f && supportGrip != null && supportGrip.IsSupportingWeapon;
            bool stagger = (State.Phase == MeleePhase.Stagger && !supportedShove) ||
                State.Phase == MeleePhase.GuardBroken || State.IsDefeated;
            bool projectileStagger = State.Phase == MeleePhase.Stagger && LastImpact.Kind == CombatImpactKind.Projectile;
            bool stepping = State.Phase == MeleePhase.Step;
            AnimationClip chosen = stepping ? (stepBlocked ? ready : stepClip) : State.IsDefeated ? defeat : State.Phase == MeleePhase.GuardBroken ? guardBreak : stagger ? hit :
                reaction != null ? reaction : State.IsKicking ? kick : State.IsCharging ? Current.Charge : State.IsAttacking ? ReleaseClip : roundEnded || weaponDropped ? rest : State.IsBlocking ? block : ready;
            // Exact-part projectile reactions own the additive pose. The shared
            // melee hit clip would otherwise bend every wounded limb identically.
            if (projectileStagger) chosen = IsFirearm ? ChoosePistolClip() : weaponDropped ? rest : ready;
            bool pistolPose = IsFirearm && !State.IsDefeated && !stepping && !stagger && reaction == null && !State.IsKicking;
            if (pistolPose) chosen = ChoosePistolClip();
            AnimationClip pistolStepUpper = IsFirearm && stepping && Firearm.AimRequested ? ChoosePistolClip() : null;
            supportGrip?.SetTarget(BodyDamage.CanUseLeftHand && (chosen == block || chosen == guardImpact),
                BodyDamage.CanUseLeftHand && !weaponDropped && !projectileStagger && chosen != rest && chosen != hit && chosen != guardBreak && !State.IsDefeated && !State.IsShoving,
                State.IsContinuation);
            PresentShovePose();
            ConfigureAttackReachPose(ReachAction);
            float progress = stagger ?
                (State.IsDefeated ? Mathf.Clamp01(defeatClock / defeat.length) : State.PhaseProgress) :
                Mathf.Repeat(poseClock, chosen.length) / chosen.length;
            if (!stagger && reaction != null) progress = Mathf.Clamp01(reactionClock / reaction.length);
            else if (State.IsCharging) progress = State.Charge01;
            else if (State.IsAttacking) progress = State.AttackProgress;
            else if (State.IsKicking) progress = KickAnimationProgress;
            if (stepping) progress = stepBlocked ? 0f : State.StepProgress;
            else if (State.Phase == MeleePhase.GuardImpact) progress = State.PhaseProgress;
            if (pistolPose) progress = PistolClipProgress(chosen);
            else if (projectileStagger) progress = IsFirearm ? PistolClipProgress(chosen) : 0f;
            bool newSwing = (State.IsCharging || State.IsAttacking || State.IsKicking) && visibleAttackSequence != State.AttackSequence;
            if (hero != null)
            {
                if (!IsAvailable) { ReleasePresentation(); return; }
                hero.SetCombatSupportGrip(this, supportGrip, weaponConstraint);
                hero.ReleaseCarryPose(this);
                if (newSwing || visibleClip != chosen.name || !hero.OwnsClip(this))
                {
                    bool releasingCharge = !newSwing && visibleClip == Current.Charge.name && State.IsAttacking && reaction == null;
                    if (!hero.TryAcquireClip(this, chosen.name, releasingCharge)) return;
                    // A stationary step starts in the exact ready pose;
                    // charge/release share their endpoint. Other changes
                    // inherit the previously displayed pose and velocity.
                    if (!releasingCharge)
                    {
                        if ((stepping && !stepBlocked || State.IsKicking) && visibleClip == ready.name &&
                            !(footwork?.RecoveryStepOwnsFeet ?? false) && motor.PlanarVelocity.sqrMagnitude < .01f)
                            CancelPoseBlend();
                        else BeginPoseBlend(TransitionSeconds(chosen));
                    }
                    visibleClip = chosen.name;
                }
                hero.SetOwnedClipLocomotion(this, false);
                if (State.IsAttacking && reaction == null && !stagger) SampleHeroRelease(progress);
                else if (State.IsCharging) SampleHeroCharge();
                else if (pistolStepUpper != null)
                    hero.SampleOwnedClipUpperTime(this, pistolStepUpper.name, progress, PistolClipProgress(pistolStepUpper));
                else hero.SampleOwnedClip(this, progress);
                hero.SetCombatBodyMotion(this, bodyMotion);
                hero.SetCombatFootwork(this, footwork);
                hero.SetCombatFirearm(this, IsFirearm ? this : null);
                ApplyMotorConstraint();
            }
            else
            {
                if (newSwing || visibleClip != chosen.name)
                {
                    if (newSwing || !(visibleClip == Current.Charge.name && State.IsAttacking && reaction == null)) BeginPoseBlend(TransitionSeconds(chosen));
                    visibleClip = chosen.name;
                }
                long sampleStart = System.Diagnostics.Stopwatch.GetTimestamp();
                SampleNpcAction(chosen, progress);
                NpcAnimationSampleTicks += System.Diagnostics.Stopwatch.GetTimestamp() - sampleStart;
            }
            visibleAttackSequence = State.AttackSequence;
            handPose.SetGrip(false, weaponDropped ? 0f : 1f);
            if (IsFirearm)
            {
                pistolLeftClosure = (pistolPose || pistolStepUpper != null) && !weaponDropped
                    ? BlendPistolClosure(PistolSupportClosure(pistolStepUpper != null ? pistolStepUpper : chosen)) : 0f;
                handPose.SetGrip(true, pistolLeftClosure);
            }
            if (npc != null)
            {
                long compositionStart = System.Diagnostics.Stopwatch.GetTimestamp();
                ApplyNpcCombatPose();
                NpcPoseCompositionTicks += System.Diagnostics.Stopwatch.GetTimestamp() - compositionStart;
                long snapshotStart = System.Diagnostics.Stopwatch.GetTimestamp();
                RememberNpcPresentedPose();
                NpcPoseSnapshotTicks += System.Diagnostics.Stopwatch.GetTimestamp() - snapshotStart;
            }
            else
            {
                PresentDamagePose();
                hero.ReapplyLatePresentationPose();
                hero.RememberOwnedRecoveryPose(this, poseClock);
            }
            footwork?.CapturePresentedContacts();
            supportGrip?.CapturePresentedBalanceContact();
            if (weaponDropped) { handPose.SetGrip(false, 0f); handPose.SetGrip(true, 0f); }
        }

        private void SampleNpcAction(AnimationClip chosen, float progress)
        {
            if (State.IsAttacking && reaction == null) SampleNpcRelease(progress);
            else if (State.IsCharging) SampleNpcReleasePose(0f, State.Charge01);
            else SampleNpcClip(chosen, progress * chosen.length);
        }

        private float TransitionSeconds(AnimationClip chosen) => IsFirearm && (chosen == pistolRaise || chosen == pistolLower)
            ? pistolRaise.length : State.Phase == MeleePhase.Stagger &&
            LastImpact.Kind == CombatImpactKind.Projectile && ImpactMotion != null && ImpactMotion.Age < .05f ? .045f : chosen == rest ? .35f :
            chosen == block || visibleClip == block.name ? .18f :
            State.IsContinuation && (chosen == Current.Charge || chosen == ReleaseClip) ? .20f : PoseBlendSeconds;

        public void ResetActor(Vector3 position, Vector3 facing)
        {
            CombatFocused = true;
            freeLocomotionReleased = false;
            presentationFrozen = false;
            guardHeld = false;
            journalActionRequest = journalQueuedRequest = 0;
            LastJournalImpactSequence = 0;
            journalRiseReason = journalFallReason = journalBlockReason = null;
            journalBlockHeld = journalBlockAllowed = journalMovementBlocked = false;
            journalContactRejectedSequence = -1; journalContactRejectedReason = null;
            journalSaturatedBuffers = 0u;
            ResetKnockdown();
            Ragdoll?.Cancel();
            ReturnWeaponToRightHand();
            RestoreWeapon();
            ReleasePresentation();
            ResetDefeat();
            ResetDamage();
            ResetShove();
            ResetKick();
            ResetAttackReach();
            supportGrip?.Reset();
            weaponConstraint?.Reset();
            if (hero != null) hero.SetCombatSupportGrip(this, supportGrip, weaponConstraint);
            State.Reset(); poseClock = 0f;
            ResetPistol();
            locomotionVelocity = Vector3.zero;
            npcPresentedPoseValid = false;
            stepClip = null; stepDirection = Vector3.zero; stepBlocked = false; pendingStepInput = Vector2.zero;
            reaction = null; reactionClock = 0f; receivedDuringStep = false;
            sweepValid = false;
            if (motor != null) motor.Teleport(position);
            else
            {
                Body.enabled = false;
                transform.position = position;
                Body.enabled = true;
            }
            transform.rotation = Quaternion.LookRotation(facing);
            ResetWeaponContacts();
            ResetCombatFacing();
            bodyMotion?.Reset();
            footwork?.Reset();
            Present();
        }

        private void ReleasePresentation(bool preservePresentedPose = false)
        {
            simulationPosePending = false;
            ResetCombatFacing();
            CancelPendingKick("presentation_released");
            ReleaseCombatAttention();
            if (hero != null) hero.ReleaseContextualFacialExpression(this);
            if (hero != null) hero.ClearCombatSupportGrip(this);
            hero?.ClearCombatFirearm(this);
            if (IsRagdollActive) { supportGrip?.Forget(); weaponConstraint?.Forget(); }
            supportGrip?.Reset();
            weaponConstraint?.Reset();
            if (IsRagdollActive) footwork?.Forget();
            else footwork?.Restore();
            ReleaseDamagePose();
            if (IsRagdollActive) bodyMotion?.Forget();
            if (hero != null) hero.ClearCombatBodyMotion(this);
            bodyMotion?.Reset();
            footwork?.Reset();
            if (handPose != null) handPose.SetGrip(false, 0f);
            CancelPoseBlend();
            if (hero != null) hero.ClearOwnedRecoveryPoseClock(this, preservePresentedPose);
            if (hero != null) { hero.ReleaseOwnedClip(this); hero.ReleaseCarryPose(this); }
            if (motor != null) motor.ReleaseMovementConstraint(this);
            motor?.SetOwnedMovementFrozen(this, false);
            visibleClip = null;
            visibleAttackSequence = 0;
        }

        private void OnDisable()
        {
            ReleaseNpcAnimation();
            CancelPendingPistolShot("disabled");
            Firearm?.CancelAction();
            pistolVisualAimProgress = 0f;
            pistolLeftClosure = pistolBlendClosure = 0f;
            if (State.IsShoving || State.IsKicking || State.Phase == MeleePhase.Step || State.HasBufferedStep)
                State.CancelAction();
            ResetShove();
            ResetKick();
            State.CancelCharge();
            ResetKnockdown();
            Ragdoll?.Cancel();
            // Scene teardown is already deactivating the arena hierarchy;
            // Unity forbids reparenting the dropped prop during that operation.
            if (gameObject.activeInHierarchy) { ReturnWeaponToRightHand(); RestoreWeapon(); }
            ReleasePresentation();
            ResetDamage();
        }

        private void OnDestroy()
        {
            ReleaseNpcAnimation();
            ResetKnockdown();
            Ragdoll?.Cancel();
            if (weaponDropped && Weapon != null) Destroy(Weapon);
            ReleasePresentation();
            damagePose?.Dispose();
            weaponConstraint?.Dispose();
            DisposeHeldWeaponPhysics();
        }
    }
}
