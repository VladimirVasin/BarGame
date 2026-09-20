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
        private AnimationClip ready, attack, block, hit, walk, guardImpact, guardBreak, recoil, reaction, defeat;
        private Transform strikeBase, strikeTip;
        private string visibleClip;
        private float poseClock, locomotionSpeed, reactionClock, pushElapsed;
        private Vector3 pushDirection;
        private readonly List<Contact> standaloneContacts = new List<Contact>(4);
        private Transform[] legs;
        private Vector3[] legPositions;
        private Quaternion[] legRotations;
        public MeleeCombatant State { get; } = new MeleeCombatant();
        public CharacterController Body { get; private set; }
        public GameObject Weapon { get; private set; }
        public bool IsHero => hero != null;
        public bool IsAvailable => isActiveAndEnabled && (hero == null ||
            (hero.CanAcquireClip(this) && !interaction.IsActive && motor.InputEnabled));

        // Finishing a round prevents further attacks, but the standing winner
        // still walks. Defeat/stagger and the active swing own their own stop.
        public float MovementScale => State.Phase switch
        {
            MeleePhase.Windup => .22f,
            MeleePhase.Active => 0f,
            MeleePhase.Recovery => Mathf.Lerp(.15f, .65f, State.PhaseProgress),
            MeleePhase.Ready => State.IsBlocking ? .45f : 1f,
            _ => 0f
        };

        internal float TurnScale => State.Phase switch
        {
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
            foreach (AnimationClip clip in new[] { ready, attack, block, hit, guardImpact, guardBreak, recoil, defeat })
                hero.Registry.RegisterRuntimeAnimation(new Player3DAnimationBinding(
                    clip.name, "Combat", clip, clip.length, clip.isLooping));
            foreach (string name in CombatAssetProvider.HeroLocomotionClipNames)
            {
                AnimationClip clip = CombatAssetProvider.LoadClip(name);
                hero.Registry.RegisterRuntimeAnimation(new Player3DAnimationBinding(
                    clip.name, "Combat", clip, clip.length, clip.isLooping));
            }
            LoadStepClips();
            Ragdoll = gameObject.AddComponent<CombatRagdoll>();
            Ragdoll.InitializeHero(player);
            AttachWeapon(hero.Registry.Anchors.RightGrip);
            hero.RegisterAccessoryRenderers(Weapon.GetComponentsInChildren<Renderer>());
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
                if (bone.name.StartsWith("thigh.", StringComparison.Ordinal) ||
                    bone.name.StartsWith("shin.", StringComparison.Ordinal) ||
                    bone.name.StartsWith("foot.", StringComparison.Ordinal) ||
                    bone.name.StartsWith("toe.", StringComparison.Ordinal)) lower.Add(bone);
            legs = lower.ToArray(); legPositions = new Vector3[legs.Length]; legRotations = new Quaternion[legs.Length];
            InitializeNpcPoseBlend();
            Ragdoll = gameObject.AddComponent<CombatRagdoll>();
            Ragdoll.InitializeOpponent(presentation, body);
            AttachWeapon(npc.RightGrip);
            Present();
        }

        private void LoadClips(bool forNpc)
        {
            ready = CombatAssetProvider.LoadClip("CombatReady", forNpc);
            attack = CombatAssetProvider.LoadClip("CombatAttack", forNpc);
            block = CombatAssetProvider.LoadClip("CombatBlock", forNpc);
            hit = CombatAssetProvider.LoadClip("CombatHit", forNpc);
            guardImpact = CombatAssetProvider.LoadClip("CombatGuardImpact", forNpc);
            guardBreak = CombatAssetProvider.LoadClip("CombatGuardBreak", forNpc);
            recoil = CombatAssetProvider.LoadClip("CombatRecoil", forNpc);
            defeat = CombatAssetProvider.LoadClip("CombatDefeat", forNpc);
        }

        private void AttachWeapon(Transform grip)
        {
            Weapon = CombatAssetProvider.CreateCrowbar(grip);
            strikeBase = CombatAssetProvider.FindAnchor(Weapon, "StrikeBase");
            strikeTip = CombatAssetProvider.FindAnchor(Weapon, "StrikeTip");
            if (strikeBase == null || strikeTip == null) throw new InvalidOperationException("Crowbar needs its authored strike anchors.");
            PrepareWeaponPhysics();
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
                float previous = Mathf.Clamp01(pushElapsed / .16f);
                pushElapsed += seconds;
                float next = Mathf.Clamp01(pushElapsed / .16f);
                float distance = .12f * ((2f * next - next * next) - (2f * previous - previous * previous));
                Body.Move(pushDirection * distance + Vector3.down * seconds);
            }
            int sequence = State.AttackSequence;
            float from = State.AttackElapsed;
            MeleePhase previousPhase = State.Phase;
            float previousStep = State.StepTravelProgress;
            MeleeAdvanceResult elapsed = State.Advance(seconds);
            if (previousPhase == MeleePhase.Step) AdvanceStepMovement(previousStep, State.StepTravelProgress);
            if (previousPhase == MeleePhase.GuardImpact && State.Phase != MeleePhase.GuardImpact && reaction == guardImpact)
                reaction = null;
            if (sequence != State.AttackSequence) { from = 0f; reaction = null; }
            if ((State.IsAttacking && reaction != recoil) || elapsed.HasActiveWindow)
                SweepWeapon(from, State.AttackElapsed, State.AttackSequence, pending);
            else sweepValid = false;
        }

        private MeleeHitResult Receive(CombatActor source, bool front)
        {
            Vector3 incoming = source.transform.position - transform.position;
            incoming.y = 0;
            MeleeHitResult result = State.ReceiveHit(source.State.Settings.Damage, source.State.Settings.BlockCost, front);
            if (result == MeleeHitResult.Ignored) return result;
            RetroAudio.PlayAt(result == MeleeHitResult.Blocked ? RetroSfxId.SpadeGlance : RetroSfxId.CoffinSettle,
                transform.position + Vector3.up, .75f);
            if (result != MeleeHitResult.Blocked && motor != null)
                motor.TryApplyExternalPush(-incoming.normalized, .12f, .16f);
            if (result != MeleeHitResult.Blocked && npc != null)
            { pushDirection = -incoming.normalized; pushElapsed = 0f; }
            reaction = result == MeleeHitResult.Blocked ? guardImpact : null;
            reactionClock = 0f;
            if (result == MeleeHitResult.GuardBroken)
                RetroAudio.PlayAt(RetroSfxId.SpadeGlance, transform.position + Vector3.up, .85f);
            if (State.IsDefeated)
                BeginDefeat(-incoming.normalized, transform.position + Vector3.up * 1.15f);
            Present();
            return result;
        }

        private bool SampleAttack(float progress)
        {
            if (hero != null)
            {
                if (visibleClip != attack.name || !hero.OwnsClip(this))
                {
                    if (!hero.TryAcquireClip(this, attack.name)) return false;
                    BeginPoseBlend();
                }
                visibleClip = attack.name;
                hero.SetOwnedClipLocomotion(this, MovementScale > 0f && motor.PlanarVelocity.sqrMagnitude > .01f);
                hero.SampleOwnedClip(this, progress);
            }
            else attack.SampleAnimation(npc.Animator.gameObject, progress * attack.length);
            return true;
        }

        public void SetLocomotion(float speed) => locomotionSpeed = speed;

        public void Present()
        {
            if (ready == null || IsRagdollActive) return;
            bool stagger = State.Phase == MeleePhase.Stagger || State.Phase == MeleePhase.GuardBroken || State.IsDefeated;
            bool stepping = State.Phase == MeleePhase.Step;
            AnimationClip chosen = stepping ? (stepBlocked ? ready : stepClip) : State.IsDefeated ? defeat : State.Phase == MeleePhase.GuardBroken ? guardBreak : stagger ? hit :
                reaction != null ? reaction : State.IsAttacking ? attack : State.IsBlocking ? block : ready;
            float progress = stagger ?
                (State.IsDefeated ? Mathf.Clamp01(defeatClock / defeat.length) : State.PhaseProgress) :
                Mathf.Repeat(poseClock, chosen.length) / chosen.length;
            if (!stagger && reaction != null) progress = Mathf.Clamp01(reactionClock / reaction.length);
            else if (State.IsAttacking) progress = State.AttackProgress;
            if (stepping) progress = stepBlocked ? 0f : State.StepProgress;
            else if (State.Phase == MeleePhase.GuardImpact) progress = State.PhaseProgress;
            if (hero != null)
            {
                if (!IsAvailable) { ReleasePresentation(); return; }
                bool fullBody = State.IsAttacking || stagger || reaction != null || stepping;
                if (fullBody)
                {
                    hero.ReleaseCarryPose(this);
                    if (visibleClip != chosen.name || !hero.OwnsClip(this))
                    {
                        if (!hero.TryAcquireClip(this, chosen.name)) return;
                        // A stationary step starts in the exact ready pose;
                        // blending it again would slide the planted support foot.
                        if (stepping && !stepBlocked && visibleClip == ready.name && motor.PlanarVelocity.sqrMagnitude < .01f)
                            CancelPoseBlend();
                        else BeginPoseBlend();
                        visibleClip = chosen.name;
                    }
                    hero.SetOwnedClipLocomotion(this, MovementScale > 0f && motor.PlanarVelocity.sqrMagnitude > .01f);
                    hero.SampleOwnedClip(this, progress);
                }
                else
                {
                    hero.ReleaseOwnedClip(this);
                    if (visibleClip != chosen.name || !hero.HasCarryPose)
                    {
                        hero.TryAcquireCarryPose(this, chosen.name);
                        BeginPoseBlend();
                    }
                    visibleClip = chosen.name;
                    hero.UpdateCarryPose(this, progress * chosen.length);
                }
                motor.SetOwnedMovementConstraint(this, MovementScale, TurnScale);
            }
            else
            {
                if (visibleClip != chosen.name) { BeginPoseBlend(); visibleClip = chosen.name; }
                bool walking = Mathf.Abs(locomotionSpeed) > .05f && MovementScale > 0f;
                if (walking)
                {
                    walk.SampleAnimation(npc.Animator.gameObject, Mathf.Repeat(poseClock * Mathf.Sign(locomotionSpeed), walk.length));
                    for (int i = 0; i < legs.Length; i++)
                    { legPositions[i] = legs[i].localPosition; legRotations[i] = legs[i].localRotation; }
                }
                chosen.SampleAnimation(npc.Animator.gameObject, progress * chosen.length);
                if (walking)
                    for (int i = 0; i < legs.Length; i++)
                    { legs[i].localPosition = legPositions[i]; legs[i].localRotation = legRotations[i]; }
                ApplyNpcPoseBlend();
            }
        }

        public void ResetActor(Vector3 position, Vector3 facing)
        {
            Ragdoll?.Cancel();
            RestoreWeapon();
            ReleasePresentation();
            ResetDefeat();
            State.Reset(); poseClock = locomotionSpeed = 0f;
            stepClip = null; stepDirection = Vector3.zero; stepBlocked = false;
            reaction = null; reactionClock = 0f; pushElapsed = .16f; pushDirection = Vector3.zero;
            sweepValid = false;
            if (motor != null) motor.Teleport(position);
            else
            {
                Body.enabled = false;
                transform.position = position;
                Body.enabled = true;
            }
            transform.rotation = Quaternion.LookRotation(facing);
            Present();
        }

        private void ReleasePresentation()
        {
            CancelPoseBlend();
            if (hero != null) { hero.ReleaseOwnedClip(this); hero.ReleaseCarryPose(this); }
            if (motor != null) motor.ReleaseMovementConstraint(this);
            visibleClip = null;
        }

        private void OnDisable()
        {
            Ragdoll?.Cancel();
            // Scene teardown is already deactivating the arena hierarchy;
            // Unity forbids reparenting the dropped prop during that operation.
            if (gameObject.activeInHierarchy) RestoreWeapon();
            ReleasePresentation();
        }

        private void OnDestroy()
        {
            Ragdoll?.Cancel();
            if (weaponDropped && Weapon != null) Destroy(Weapon);
            ReleasePresentation();
        }
    }
}
