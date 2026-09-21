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
        private AnimationClip ready, rest, block, hit, walk, guardImpact, guardBreak, reaction, defeat;
        private CombatSupportGrip supportGrip;
        private bool heroMovingPose, npcMovingPose;
        private Transform strikeBase, strikeTip;
        private string visibleClip;
        private float poseClock, locomotionSpeed, reactionClock, pushElapsed, pushDistance = .12f, pushDuration = .16f;
        private Vector3 pushDirection;
        private readonly List<Contact> standaloneContacts = new List<Contact>(4);
        private Transform[] legs;
        private Vector3[] legPositions;
        private Quaternion[] legRotations;
        public MeleeCombatant State { get; } = new MeleeCombatant();
        public CharacterController Body { get; private set; }
        public GameObject Weapon { get; private set; }
        public bool IsHero => hero != null;
        public Vector3 SupportGripWorldPosition => supportGrip != null ? supportGrip.Target : transform.position;
        public float SupportGripWeight => supportGrip?.Weight ?? 0f;
        public bool IsAvailable => isActiveAndEnabled && (hero == null ||
            (hero.CanAcquireClip(this) && !interaction.IsActive && motor.InputEnabled));

        // Finishing a round prevents further attacks, but the standing winner
        // still walks. Defeat/stagger and the active swing own their own stop.
        public float MovementScale => State.Phase switch
        {
            MeleePhase.Charging => .22f,
            // The swing gathers itself instead of snapping from a walk to a halt.
            MeleePhase.Windup => Mathf.Lerp(.55f, .2f, State.PhaseProgress),
            MeleePhase.Active => 0f,
            MeleePhase.Recovery => Mathf.Lerp(.15f, .65f, State.PhaseProgress),
            MeleePhase.Ready => State.IsBlocking ? .45f : 1f,
            _ => 0f
        };

        internal float TurnScale => State.Phase switch
        {
            MeleePhase.Charging => .2f,
            MeleePhase.Windup => .2f,
            MeleePhase.Active => 0f,
            MeleePhase.Recovery => Mathf.Lerp(.15f, .75f, State.PhaseProgress),
            MeleePhase.Ready => 1f,
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
            foreach (string name in CombatAssetProvider.HeroLocomotionClipNames)
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
            walk = npc.GetClip(VillageResidentAction.Walk);
            npc.ReleaseAnimation();
            LoadClips(true);
            var lower = new List<Transform>();
            foreach (Transform bone in npc.ModelRoot.GetComponentsInChildren<Transform>())
                if (bone.name == "pelvis" || bone.name.StartsWith("thigh.", StringComparison.Ordinal) ||
                    bone.name.StartsWith("shin.", StringComparison.Ordinal) ||
                    bone.name.StartsWith("foot.", StringComparison.Ordinal) ||
                    bone.name.StartsWith("toe.", StringComparison.Ordinal)) lower.Add(bone);
            legs = lower.ToArray(); legPositions = new Vector3[legs.Length]; legRotations = new Quaternion[legs.Length];
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
            if (hero != null) hero.SetCombatSupportGrip(this, supportGrip);
        }

        public bool TryAttack()
        {
            if (roundEnded || !IsAvailable || !GameInput.CanRead(GameInputContext.Gameplay) || !State.TryStartAttack()) return false;
            reaction = null;
            Present();
            return true;
        }

        public bool RequestAttack()
        {
            if (roundEnded || !IsAvailable || !GameInput.CanRead(GameInputContext.Gameplay)) return false;
            int previous = State.AttackSequence;
            if (!State.RequestAttack()) return false;
            if (previous != State.AttackSequence) reaction = null;
            Present();
            return true;
        }

        public void SetBlock(bool held) => State.SetBlocking(held && !roundEnded && IsAvailable);

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
                AdvanceSimulation(step, standaloneContacts);
                foreach (Contact contact in standaloneContacts) contact.Apply();
                remaining -= step;
            }
            Present();
        }

        internal void AdvanceSimulation(float seconds, List<Contact> pending)
        {
            if (State.IsDefeated) { AdvanceDefeat(seconds); return; }
            if (!IsAvailable)
            {
                State.CancelAction(); reaction = null; sweepValid = false;
                ReleasePresentation(); return;
            }
            AdvanceVisualClock(seconds);
            if (npc != null && Body.enabled)
            {
                float previous = Mathf.Clamp01(pushElapsed / pushDuration);
                pushElapsed += seconds;
                float next = Mathf.Clamp01(pushElapsed / pushDuration);
                float distance = pushDistance * ((2f * next - next * next) - (2f * previous - previous * previous));
                Body.Move(pushDirection * distance + Vector3.down * seconds);
            }
            int sequence = State.AttackSequence;
            float from = State.AttackElapsed;
            MeleePhase previousPhase = State.Phase;
            float previousStep = State.StepTravelProgress;
            MeleeAdvanceResult elapsed = State.Advance(seconds);
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
                SweepWeapon(from, State.AttackElapsed, State.AttackSequence, pending);
            else sweepValid = false;
        }

        private MeleeHitResult Receive(CombatActor source, bool front, int sequence, Vector3 point, Vector3 normal, Vector3 direction,
            float damage, float blockCost, float power)
        {
            Vector3 incoming = source.transform.position - transform.position;
            incoming.y = 0;
            Vector3 away = incoming.sqrMagnitude > .0001f ? -incoming.normalized : -transform.forward;
            float healthBefore = State.Health;
            MeleeHitResult result = State.ReceiveHit(damage, blockCost, front, power);
            if (result == MeleeHitResult.Ignored) return result;
            // Weight lives in time and motion: the body is the loudest cue, a block
            // moves both fighters, a parry throws the attacker's weapon wide.
            switch (result)
            {
                case MeleeHitResult.Hit:
                    RetroAudio.PlayAt(RetroSfxId.SpadeBite, point, Mathf.Lerp(.8f, 1f, power));
                    Shove(away, source.State.IsChained ? .20f : Mathf.Lerp(.15f, .28f, power), Mathf.Lerp(.16f, .20f, power));
                    reaction = null;
                    break;
                case MeleeHitResult.GuardBroken:
                    RetroAudio.PlayAt(RetroSfxId.SpadeBite, point, 1f);
                    RetroAudio.PlayAt(RetroSfxId.StoneTamp, transform.position + Vector3.up, .6f);
                    Shove(away, .35f, .22f);
                    reaction = null;
                    break;
                case MeleeHitResult.Blocked:
                    RetroAudio.PlayAt(RetroSfxId.SpadeGlance, point, .55f);
                    Shove(away, .06f, .12f);
                    source.Shove(-away, .04f, .12f);
                    damagePose?.Hit(away, .3f);
                    reaction = guardImpact;
                    break;
                case MeleeHitResult.Parried:
                    RetroAudio.PlayAt(RetroSfxId.SpadeGlance, point, 1f);
                    RetroAudio.PlayAt(RetroSfxId.StoneTamp, point, .9f);
                    reaction = guardImpact;
                    break;
            }
            reactionClock = 0f;
            if (State.IsDefeated)
                BeginDefeat(away, point);
            PublishImpact(new CombatImpact(source, this, sequence, point, normal, direction,
                healthBefore, State.Health, result));
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
            if (motor != null) motor.TryApplyExternalPush(direction, distance, duration);
            else if (npc != null)
            {
                pushDirection = direction;
                pushDistance = distance;
                pushDuration = duration;
                pushElapsed = 0f;
            }
        }

        private bool SampleAttack(float progress)
        {
            supportGrip?.Restore();
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
                bool movingPose = HeroUsesGait(false);
                if (heroMovingPose != movingPose) BeginPoseBlend();
                heroMovingPose = movingPose;
                hero.SetOwnedClipLocomotion(this, movingPose);
                SampleHeroRelease(progress);
                hero.SetCombatBodyMotion(this, bodyMotion);
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
                SampleNpcAction(chosen, progress, false);
                ApplyNpcCombatPose();
            }
            handPose.SetGrip(false, 1f);
            return true;
        }

        public void SetLocomotion(float speed) => SetLocomotion(transform.forward * speed);

        internal void SetPresentationFrozen(bool frozen)
        {
            if (hero != null) hero.SetOwnedPresentationFrozen(this, frozen);
        }

        public void SetLocomotion(Vector3 velocity)
        {
            velocity.y = 0f;
            locomotionVelocity = velocity;
            locomotionSpeed = velocity.magnitude * (Vector3.Dot(velocity, transform.forward) < 0f ? -1f : 1f);
        }

        private bool HeroUsesGait(bool stepping) => !stepping && MovementScale > 0f &&
            (motor.PlanarVelocity.sqrMagnitude > .0025f || hero.LocomotionBlend > .05f);

        public void Present()
        {
            if (ready == null || IsRagdollActive) return;
            // Pause temporarily owns input, not the combat rig. Retain the
            // sampled pose/transition so a paused read cannot release the clip.
            if (PauseMenuController.IsAnyPaused) return;
            supportGrip?.Restore();
            damagePose?.Restore();
            bodyMotion?.Restore();
            bool stagger = State.Phase == MeleePhase.Stagger || State.Phase == MeleePhase.GuardBroken || State.IsDefeated;
            bool stepping = State.Phase == MeleePhase.Step;
            AnimationClip chosen = stepping ? (stepBlocked ? ready : stepClip) : State.IsDefeated ? defeat : State.Phase == MeleePhase.GuardBroken ? guardBreak : stagger ? hit :
                reaction != null ? reaction : State.IsCharging ? Current.Charge : State.IsAttacking ? ReleaseClip : roundEnded ? rest : State.IsBlocking ? block : ready;
            supportGrip?.SetTarget(chosen == block || chosen == guardImpact, chosen != rest && !State.IsDefeated);
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
                hero.SetCombatSupportGrip(this, supportGrip);
                bool movingPose = HeroUsesGait(stepping);
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
                if (heroMovingPose != movingPose) BeginPoseBlend();
                heroMovingPose = movingPose;
                hero.SetOwnedClipLocomotion(this, movingPose);
                if (State.IsAttacking && reaction == null && !stagger) SampleHeroRelease(progress);
                else if (State.IsCharging) SampleHeroCharge();
                else hero.SampleOwnedClip(this, progress);
                hero.SetCombatBodyMotion(this, bodyMotion);
                motor.SetOwnedMovementConstraint(this, MovementScale, TurnScale);
            }
            else
            {
                if (visibleClip != chosen.name)
                {
                    if (!(visibleClip == Current.Charge.name && State.IsAttacking && reaction == null)) BeginPoseBlend(TransitionSeconds(chosen));
                    visibleClip = chosen.name;
                }
                SampleNpcAction(chosen, progress, stepping);
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
        }

        private void SampleNpcAction(AnimationClip chosen, float progress, bool stepping)
        {
            bool walking = !stepping && Mathf.Abs(locomotionSpeed) > .05f && MovementScale > 0f;
            if (walking != npcMovingPose) BeginPoseBlend(.18f);
            npcMovingPose = walking;
            if (walking)
            {
                // Preserve the walking pelvis along with its legs; a lowered
                // standing pelvis would push both soles through the ground.
                walk.SampleAnimation(npc.Animator.gameObject, Mathf.Repeat(poseClock * Mathf.Sign(locomotionSpeed), walk.length));
                for (int i = 0; i < legs.Length; i++)
                { legPositions[i] = legs[i].localPosition; legRotations[i] = legs[i].localRotation; }
            }
            if (State.IsAttacking && reaction == null) SampleNpcRelease(progress);
            else chosen.SampleAnimation(npc.Animator.gameObject, progress * chosen.length);
            if (walking)
                for (int i = 0; i < legs.Length; i++)
                { legs[i].localPosition = legPositions[i]; legs[i].localRotation = legRotations[i]; }
        }

        private float TransitionSeconds(AnimationClip chosen) => chosen == rest ? .35f :
            chosen == block || visibleClip == block.name ? .18f : PoseBlendSeconds;

        public void ResetActor(Vector3 position, Vector3 facing)
        {
            Ragdoll?.Cancel();
            RestoreWeapon();
            ReleasePresentation();
            ResetDefeat();
            ResetDamage();
            supportGrip?.Reset();
            if (hero != null) hero.SetCombatSupportGrip(this, supportGrip);
            heroMovingPose = npcMovingPose = false;
            State.Reset(); poseClock = locomotionSpeed = 0f;
            locomotionVelocity = Vector3.zero;
            npcPresentedPoseValid = false;
            stepClip = null; stepDirection = Vector3.zero; stepBlocked = false; pendingStepInput = Vector2.zero;
            reaction = null; reactionClock = 0f; pushElapsed = pushDuration; pushDirection = Vector3.zero;
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
            Present();
        }

        private void ReleasePresentation()
        {
            if (hero != null) hero.ClearCombatSupportGrip(this);
            if (IsRagdollActive) supportGrip?.Forget();
            supportGrip?.Reset();
            ReleaseDamagePose();
            if (IsRagdollActive) bodyMotion?.Forget();
            if (hero != null) hero.ClearCombatBodyMotion(this);
            bodyMotion?.Reset();
            if (handPose != null) handPose.SetGrip(false, 0f);
            CancelPoseBlend();
            if (hero != null) hero.ClearOwnedRecoveryPoseClock(this);
            if (hero != null) { hero.ReleaseOwnedClip(this); hero.ReleaseCarryPose(this); }
            if (motor != null) motor.ReleaseMovementConstraint(this);
            visibleClip = null;
        }

        private void OnDisable()
        {
            State.CancelCharge();
            Ragdoll?.Cancel();
            // Scene teardown is already deactivating the arena hierarchy;
            // Unity forbids reparenting the dropped prop during that operation.
            if (gameObject.activeInHierarchy) RestoreWeapon();
            ReleasePresentation();
            ResetDamage();
        }

        private void OnDestroy()
        {
            Ragdoll?.Cancel();
            if (weaponDropped && Weapon != null) Destroy(Weapon);
            ReleasePresentation();
            damagePose?.Dispose();
        }
    }
}
