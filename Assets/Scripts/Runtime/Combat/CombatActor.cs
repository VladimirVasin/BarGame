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
        private float poseClock, reactionClock;
        private bool receivedDuringStep;
        private readonly List<Contact> standaloneContacts = new List<Contact>(4);
        public MeleeCombatant State { get; } = new MeleeCombatant();
        public CharacterController Body { get; private set; }
        public GameObject Weapon { get; private set; }
        public bool IsHero => hero != null;
        public Vector3 SupportGripWorldPosition => supportGrip != null ? supportGrip.Target : transform.position;
        public float SupportGripWeight => supportGrip?.Weight ?? 0f;
        public CombatArmSupportState SupportArmState => supportGrip?.State ?? CombatArmSupportState.SupportingWeapon;
        public bool IsAvailable => isActiveAndEnabled && (hero == null ||
            (hero.CanAcquireClip(this) && !interaction.IsActive && motor.InputEnabled));

        // Finishing a round prevents further attacks, but the standing winner
        // still walks. Defeat/stagger and the active swing own their own stop.
        public float MovementScale => IsKnockedDown || (ImpactMotion != null && ImpactMotion.Velocity.sqrMagnitude > .04f) ? 0f : State.Phase switch
        {
            MeleePhase.Charging => .22f,
            // The swing gathers itself instead of snapping from a walk to a halt.
            MeleePhase.Windup => Mathf.Lerp(.55f, .2f, State.PhaseProgress),
            MeleePhase.Active => 0f,
            MeleePhase.Recovery => Mathf.Lerp(.15f, .65f, State.PhaseProgress),
            MeleePhase.Ready => State.IsBlocking ? .45f : 1f,
            _ => 0f
        };

        internal float TurnScale => IsKnockedDown || (ImpactMotion != null && ImpactMotion.BalanceLoad > .45f) ? 0f : State.Phase switch
        {
            MeleePhase.Charging => .2f,
            MeleePhase.Windup => .2f,
            MeleePhase.Active => 0f,
            MeleePhase.Recovery => Mathf.Lerp(.15f, .75f, State.PhaseProgress),
            MeleePhase.Ready => 1f,
            // A rocked body still brings its head round toward the blow at a third of
            // the free rate; the step's planted soles, the block's impact and the arc keep zero.
            MeleePhase.Stagger or MeleePhase.GuardBroken => .35f,
            _ => 0f
        };

        public void InitializeHero(PlayerRuntime player)
        {
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
            hero.RegisterAccessoryRenderers(Weapon.GetComponentsInChildren<Renderer>());
            InitializeDamagePose();
            Present();
        }

        public void InitializeOpponent(VillageResidentPresentation presentation, CharacterController body)
        {
            npc = presentation;
            Body = body;
            npc.ReleaseAnimation();
            LoadClips(true);
            InitializeNpcPoseBlend();
            InitializeNpcChargeBlend();
            LoadStepClips(true);
            Ragdoll = gameObject.AddComponent<CombatRagdoll>();
            Ragdoll.InitializeOpponent(presentation, body);
            AttachWeapon(npc.RightGrip);
            InitializeDamagePose();
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
            Weapon = CombatAssetProvider.CreateCrowbar(grip, handPose);
            strikeBase = CombatAssetProvider.FindAnchor(Weapon, "StrikeBase");
            strikeTip = CombatAssetProvider.FindAnchor(Weapon, "StrikeTip");
            if (strikeBase == null || strikeTip == null) throw new InvalidOperationException("Crowbar needs its authored strike anchors.");
            PrepareWeaponPhysics();
            supportGrip = new CombatSupportGrip(DamageRigRoot, transform, handPose, Weapon.transform);
            supportGrip.JournalActor = this;
            weaponConstraint = new CombatWeaponConstraint(this);
            supportGrip.SetArmClearance(weaponConstraint.IsSupportArmPathClear);
            if (hero != null) hero.SetCombatSupportGrip(this, supportGrip, weaponConstraint);
        }

        internal bool HasTwoHandSupport => !IsKnockedDown && !(ImpactMotion?.RecoveryInProgress ?? false) &&
            (supportGrip == null || supportGrip.IsSupportingWeapon);
        internal CombatFootwork Footwork => footwork;
        internal CombatSupportGrip SupportGrip => supportGrip;

        public bool TryAttack()
        {
            int request = JournalCommand("attack_immediate");
            if (roundEnded) return JournalCommandResult(request, "rejected", "round_ended");
            if (!IsAvailable) return JournalCommandResult(request, "rejected", "actor_unavailable");
            if (!GameInput.CanRead(GameInputContext.Gameplay)) return JournalCommandResult(request, "rejected", "input_gate");
            if (CheckShoveRange(request)) return TryBeginShove(request);
            if (!HasTwoHandSupport) return JournalCommandResult(request, "rejected", "two_hand_support");
            if (!State.TryStartAttack()) return JournalRulesRejected(request, State.Settings.AttackCost, false);
            reaction = null;
            Present();
            return JournalCommandResult(request, "started", "attack");
        }

        public bool RequestAttack()
        {
            int request = JournalCommand("attack");
            if (roundEnded) return JournalCommandResult(request, "rejected", "round_ended");
            if (!IsAvailable) return JournalCommandResult(request, "rejected", "actor_unavailable");
            if (!GameInput.CanRead(GameInputContext.Gameplay)) return JournalCommandResult(request, "rejected", "input_gate");
            if (CheckShoveRange(request)) return TryBeginShove(request);
            if (!HasTwoHandSupport) return JournalCommandResult(request, "rejected", "two_hand_support");
            int previous = State.AttackSequence;
            if (!State.RequestAttack()) return JournalRulesRejected(request, State.Settings.AttackCost, true);
            if (previous != State.AttackSequence) reaction = null;
            Present();
            return JournalCommandResult(request, previous == State.AttackSequence ? "queued" : "started", "attack");
        }

        public void SetBlock(bool held)
        {
            string reason = !held ? "released" : roundEnded ? "round_ended" : !IsAvailable ? "actor_unavailable" :
                !HasTwoHandSupport ? "two_hand_support" : "guard";
            bool allowed = reason == "guard";
            bool changed = held != journalBlockHeld || allowed != journalBlockAllowed || reason != journalBlockReason;
            int request = changed ? JournalCommand("block") : 0;
            State.SetBlocking(allowed);
            if (changed)
            {
                string result = !held ? "released" : !allowed || State.IsDefeated || State.IsKnockedDown ? "rejected" :
                    State.IsBlocking ? "started" : "queued";
                JournalCommandResult(request, result, allowed && (State.IsDefeated || State.IsKnockedDown) ? "rules_rejected" : reason, trackAction: false);
                journalBlockHeld = held; journalBlockAllowed = allowed; journalBlockReason = reason;
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
                AdvanceSimulation(step);
                Present();
                CaptureContactPose();
                contactTarget?.CaptureContactPose();
                CollectContacts(standaloneContacts);
                CollectShoveContacts(standaloneShoves);
                foreach (Contact contact in standaloneContacts) contact.Apply();
                foreach (ShoveContact contact in standaloneShoves) contact.Apply();
                remaining -= step;
            }
            Present();
        }

        internal void AdvanceSimulation(float seconds)
        {
            collectSweep = false;
            collectShove = false;
            if (AdvanceKnockdown(seconds)) return;
            if (State.IsDefeated) { AdvanceDefeat(seconds); return; }
            if (!IsAvailable)
            {
                State.CancelAction(); reaction = null; sweepValid = false;
                ReleasePresentation(); return;
            }
            AdvanceVisualClock(seconds);
            AdvanceImpactMotion(seconds);
            if (State.Phase == MeleePhase.Windup && InShoveRange && !TryBeginShove()) State.CancelAction();
            int sequence = State.AttackSequence;
            float shoveFrom = State.ShoveElapsed;
            bool wasShoving = State.IsShoving;
            float from = State.AttackElapsed;
            MeleePhase previousPhase = State.Phase;
            float previousStep = State.StepTravelProgress;
            MeleeAdvanceResult elapsed = State.Advance(seconds);
            if (sequence != State.AttackSequence && journalQueuedRequest != 0)
            {
                journalActionRequest = journalQueuedRequest; journalQueuedRequest = 0;
                JournalEvent("buffer_started", action: State.AttackSequence, request: journalActionRequest,
                    f0: GameLog.Field("phase", (int)State.Phase));
            }
            collectShove = wasShoving && shoveFrom < State.Settings.ShoveContactSeconds &&
                State.ShoveElapsed >= State.Settings.ShoveContactSeconds;
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
            if ((State.IsAttacking && !IsRecoil(reaction)) || elapsed.HasActiveWindow)
            {
                collectSweep = true;
                sweepFrom = from;
                sweepTo = State.AttackElapsed;
                pendingSequence = State.AttackSequence;
            }
            else sweepValid = false;
            footwork?.Advance(seconds, State);
            CompleteImpactRecoveryStep(seconds);
        }

        private MeleeHitResult Receive(CombatActor source, bool front, int sequence, Vector3 point, Vector3 normal, Vector3 direction,
            float damage, float blockCost, float power, MeleeHitLocation location,
            Player3DAnatomicalPart part = Player3DAnatomicalPart.Torso, Vector3 localPoint = default, float weaponSpeed = 0f)
        {
            receivedDuringStep = State.Phase == MeleePhase.Step;
            float healthBefore = State.Health;
            MeleeHitResult result = State.ReceiveHit(damage, blockCost, front, power, location);
            if (result == MeleeHitResult.Ignored) return result;
            // Weight lives in time and motion: the body is the loudest cue, a block
            // moves both fighters, a parry throws the attacker's weapon wide.
            switch (result)
            {
                case MeleeHitResult.Hit:
                    RetroAudio.PlayAt(RetroSfxId.SpadeBite, point, Mathf.Lerp(.8f, 1f, power));
                    reaction = null;
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
            reactionClock = 0f;
            PublishImpact(new CombatImpact(source, this, sequence, point, normal, direction,
                healthBefore, State.Health, result, location, power, part, localPoint, weaponSpeed,
                CombatImpactMotion.ResolveImpulse(direction, power, weaponSpeed, result)));
            if (State.IsDefeated) BeginDefeat(direction, point);
            receivedDuringStep = false;
            Present();
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
            previewWorldBlocked = false;
            using var weaponPreview = weaponConstraint.BeginContactPreview();
            supportGrip?.Restore();
            weaponConstraint?.Restore();
            footwork?.Restore();
            damagePose?.Restore();
            bodyMotion?.Restore();
            AnimationClip chosen = ReleaseClip;
            supportGrip?.SetTarget(false, true);
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
            return true;
        }

        public void SetLocomotion(float speed) => SetLocomotion(transform.forward * speed);

        internal void SetPresentationFrozen(bool frozen)
        {
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
            if (ready == null || winnerPresentationReleased) return;
            if (PresentKnockdown() || IsRagdollActive) return;
            // Pause temporarily owns input, not the combat rig. Retain the
            // sampled pose/transition so a paused read cannot release the clip.
            if (PauseMenuController.IsAnyPaused) return;
            supportGrip?.Restore();
            weaponConstraint?.Restore();
            footwork?.Restore();
            damagePose?.Restore();
            bodyMotion?.Restore();
            bool stagger = State.Phase == MeleePhase.Stagger || State.Phase == MeleePhase.GuardBroken || State.IsDefeated;
            bool stepping = State.Phase == MeleePhase.Step;
            AnimationClip chosen = stepping ? (stepBlocked ? ready : stepClip) : State.IsDefeated ? defeat : State.Phase == MeleePhase.GuardBroken ? guardBreak : stagger ? hit :
                reaction != null ? reaction : State.IsCharging ? Current.Charge : State.IsAttacking ? ReleaseClip : roundEnded ? rest : State.IsBlocking ? block : ready;
            supportGrip?.SetTarget(chosen == block || chosen == guardImpact,
                chosen != rest && chosen != hit && chosen != guardBreak && !State.IsDefeated && !State.IsShoving);
            PresentShovePose();
            float progress = stagger ?
                (State.IsDefeated ? Mathf.Clamp01(defeatClock / defeat.length) : State.PhaseProgress) :
                Mathf.Repeat(poseClock, chosen.length) / chosen.length;
            if (!stagger && reaction != null) progress = Mathf.Clamp01(reactionClock / reaction.length);
            else if (State.IsCharging) progress = State.Charge01;
            else if (State.IsAttacking) progress = State.AttackProgress;
            if (stepping) progress = stepBlocked ? 0f : State.StepProgress;
            else if (State.Phase == MeleePhase.GuardImpact) progress = State.PhaseProgress;
            if (hero != null)
            {
                if (!IsAvailable) { ReleasePresentation(); return; }
                hero.SetCombatSupportGrip(this, supportGrip, weaponConstraint);
                hero.ReleaseCarryPose(this);
                if (visibleClip != chosen.name || !hero.OwnsClip(this))
                {
                    bool releasingCharge = visibleClip == Current.Charge.name && State.IsAttacking && reaction == null;
                    if (!hero.TryAcquireClip(this, chosen.name, releasingCharge)) return;
                    // A stationary step starts in the exact ready pose;
                    // charge/release share their endpoint. Other changes
                    // inherit the previously displayed pose and velocity.
                    if (!releasingCharge)
                    {
                        if (stepping && !stepBlocked && visibleClip == ready.name && motor.PlanarVelocity.sqrMagnitude < .01f)
                            CancelPoseBlend();
                        else BeginPoseBlend(TransitionSeconds(chosen));
                    }
                    visibleClip = chosen.name;
                }
                hero.SetOwnedClipLocomotion(this, false);
                if (State.IsAttacking && reaction == null && !stagger) SampleHeroRelease(progress);
                else if (State.IsCharging) SampleHeroCharge();
                else hero.SampleOwnedClip(this, progress);
                hero.SetCombatBodyMotion(this, bodyMotion);
                hero.SetCombatFootwork(this, footwork);
                motor.SetOwnedMovementConstraint(this, MovementScale, TurnScale);
            }
            else
            {
                if (visibleClip != chosen.name)
                {
                    if (!(visibleClip == Current.Charge.name && State.IsAttacking && reaction == null)) BeginPoseBlend(TransitionSeconds(chosen));
                    visibleClip = chosen.name;
                }
                SampleNpcAction(chosen, progress);
            }
            handPose.SetGrip(false, 1f);
            if (npc != null)
            {
                ApplyNpcCombatPose();
                RememberNpcPresentedPose();
            }
            else
            {
                PresentDamagePose();
                hero.ReapplyLatePresentationPose();
                hero.RememberOwnedRecoveryPose(this, poseClock);
            }
            footwork?.CapturePresentedContacts();
            supportGrip?.CapturePresentedBalanceContact();
        }

        private void SampleNpcAction(AnimationClip chosen, float progress)
        {
            if (State.IsAttacking && reaction == null) SampleNpcRelease(progress);
            else if (State.IsCharging) SampleNpcReleasePose(0f, State.Charge01);
            else chosen.SampleAnimation(npc.Animator.gameObject, progress * chosen.length);
        }

        private float TransitionSeconds(AnimationClip chosen) => chosen == rest ? .35f :
            chosen == block || visibleClip == block.name ? .18f : PoseBlendSeconds;

        public void ResetActor(Vector3 position, Vector3 facing)
        {
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
            supportGrip?.Reset();
            weaponConstraint?.Reset();
            if (hero != null) hero.SetCombatSupportGrip(this, supportGrip, weaponConstraint);
            State.Reset(); poseClock = 0f;
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
            bodyMotion?.Reset();
            footwork?.Reset();
            Present();
        }

        private void ReleasePresentation()
        {
            if (hero != null) hero.ReleaseContextualFacialExpression(this);
            if (hero != null) hero.ClearCombatSupportGrip(this);
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
            if (hero != null) hero.ClearOwnedRecoveryPoseClock(this);
            if (hero != null) { hero.ReleaseOwnedClip(this); hero.ReleaseCarryPose(this); }
            if (motor != null) motor.ReleaseMovementConstraint(this);
            visibleClip = null;
        }

        private void OnDisable()
        {
            if (State.IsShoving) State.CancelAction();
            ResetShove();
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
