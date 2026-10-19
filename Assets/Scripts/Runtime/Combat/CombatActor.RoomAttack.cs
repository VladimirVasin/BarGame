using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum CombatRoomAttackPose { PointHero, PointChest, Laugh }

    public sealed partial class CombatActor
    {
        private AnimationClip roomPointHero, roomPointChest, roomLaugh, roomHeadPain;
        private SkinnedMeshRenderer[] roomPointingRenderers;
        private Mesh[] roomPointingMeshes;
        private int[] roomPointingShapes;
        private AudioSource roomLaughVoice;
        private CombatRoomAttackPose roomAttackPose;
        private float roomAttackPoseProgress, roomHeadPainProgress;
        private bool roomAttackPoseActive, roomHeadPainActive, roomLaughStarted;
        private int roomPoseImpactCount, roomHeadPainImpactCount;
        private CombatActor lastRoomDamageSource;
        private int lastRoomDamageSequence = int.MinValue;

        internal bool RoomAttackPoseActive => roomAttackPoseActive;
        internal bool RoomHeadPainActive => roomHeadPainActive;
        internal bool RoomLaughRecoveryActive => roomAttackPoseActive && roomAttackPose == CombatRoomAttackPose.Laugh;

        /// <summary>Prepare once before the camera/input loan; sampling does not load or allocate assets.</summary>
        internal bool PrepareRoomAttackPresentation()
        {
            if (!IsRoom || DamageRigRoot == null) return false;
            roomPointHero ??= CombatRoomAttackAssetProvider.LoadNpcClip(CombatRoomAttackAssetProvider.PointHeroClip);
            roomPointChest ??= CombatRoomAttackAssetProvider.LoadNpcClip(CombatRoomAttackAssetProvider.PointChestClip);
            roomLaugh ??= CombatRoomAttackAssetProvider.LoadNpcClip(CombatRoomAttackAssetProvider.LaughClip);
            NpcAnimationClip(roomPointHero);
            NpcAnimationClip(roomPointChest);
            NpcAnimationClip(roomLaugh);
            if (roomPointingRenderers == null)
            {
                string[] names = { "GEO_HandFinger1.R", "GEO_HandFinger2.R", "GEO_HandFinger3.R", "GEO_HandThumb.R" };
                roomPointingRenderers = new SkinnedMeshRenderer[names.Length];
                roomPointingMeshes = new Mesh[names.Length];
                roomPointingShapes = new int[names.Length];
                SkinnedMeshRenderer[] surfaces = DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (int i = 0; i < names.Length; i++)
                    foreach (SkinnedMeshRenderer surface in surfaces)
                        if (surface.name == names[i]) { roomPointingRenderers[i] = surface; break; }
            }
            for (int i = 0; i < roomPointingRenderers.Length; i++)
            {
                SkinnedMeshRenderer surface = roomPointingRenderers[i];
                if (surface == null || surface.sharedMesh == null) return false;
                Mesh mesh = surface.sharedMesh;
                if (roomPointingMeshes[i] == mesh) continue;
                int shape = -1;
                for (int candidate = 0; candidate < mesh.blendShapeCount; candidate++)
                    if (mesh.GetBlendShapeName(candidate).EndsWith("RoomPoint", StringComparison.Ordinal))
                    { shape = candidate; break; }
                if (shape < 0) return false;
                roomPointingMeshes[i] = mesh;
                roomPointingShapes[i] = shape;
            }
            if (roomLaughVoice == null)
            {
                var host = new GameObject("Room laughter");
                host.transform.SetParent(DamageRigRoot, false);
                roomLaughVoice = host.AddComponent<AudioSource>();
                roomLaughVoice.playOnAwake = roomLaughVoice.loop = false;
                CityWorkAudio.Configure(roomLaughVoice, true, 7200f, 14f, false);
                roomLaughVoice.minDistance = .9f;
                roomLaughVoice.ignoreListenerPause = false;
                roomLaughVoice.clip = CombatRoomAttackAssetProvider.LoadLaughAudio();
                roomLaughVoice.volume = .7f;
                roomLaughVoice.priority = 78;
            }
            return true;
        }

        internal bool TryBeginRoomAttackPose()
        {
            if (roomAttackPoseActive || !IsRoom || !isActiveAndEnabled || !IsAvailable ||
                State.Phase != MeleePhase.Ready || !CanAttemptBodyAction || !HasAttackBalance ||
                !BodyDamage.CanUseRightHand || !PrepareRoomAttackPresentation()) return false;
            State.CancelAction();
            roomAttackPoseActive = true;
            roomPoseImpactCount = ReceivedImpactCount;
            roomAttackPose = CombatRoomAttackPose.PointHero;
            roomAttackPoseProgress = 0f;
            roomLaughStarted = false;
            zombieWalkingRequested = false;
            locomotionVelocity = Vector3.zero;
            collectSweep = sweepValid = zombieContactPoseValid = false;
            zombieAudio?.BodySource?.Stop();
            CancelPoseBlend();
            return SampleRoomAttackPose(roomAttackPose, 0f);
        }

        internal bool SampleRoomAttackPose(CombatRoomAttackPose pose, float normalizedTime)
        {
            if (!roomAttackPoseActive || !RoomSpecialBodyAvailable || ReceivedImpactCount != roomPoseImpactCount)
            { EndRoomAttackPose(); return false; }
            if (pose < CombatRoomAttackPose.PointHero || pose > CombatRoomAttackPose.Laugh ||
                float.IsNaN(normalizedTime) || float.IsInfinity(normalizedTime)) return false;
            if (pose != roomAttackPose && pose != CombatRoomAttackPose.Laugh)
            { roomLaughVoice?.Stop(); roomLaughStarted = false; }
            roomAttackPose = pose;
            roomAttackPoseProgress = Mathf.Clamp01(normalizedTime);
            if (pose == CombatRoomAttackPose.Laugh && !roomLaughStarted)
            {
                roomLaughStarted = true;
                roomLaughVoice?.Play();
                if (presentationFrozen || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused)
                    roomLaughVoice?.Pause();
            }
            if (!PauseMenuController.IsAnyPaused && !GameTimeScaleRuntime.IsPaused) PresentRoomAttackPose();
            return roomAttackPoseActive;
        }

        internal bool BeginRoomLaughRecovery() => SampleRoomAttackPose(CombatRoomAttackPose.Laugh, 0f);

        internal void EndRoomAttackPose()
        {
            if (!roomAttackPoseActive && !roomLaughStarted) return;
            roomAttackPoseActive = roomLaughStarted = false;
            roomAttackPoseProgress = 0f;
            SetRoomPointingWeight(0f);
            roomLaughVoice?.Stop();
            visibleClip = null;
        }

        private bool RoomSpecialBodyAvailable => isActiveAndEnabled && !State.IsDefeated &&
            !IsKnockedDown && !IsRagdollActive && BodyDamage.CanStand && BodyDamage.CanUseRightHand;

        private bool PresentRoomAttackPose()
        {
            if (!roomAttackPoseActive) return false;
            if (!RoomSpecialBodyAvailable || ReceivedImpactCount != roomPoseImpactCount)
            { EndRoomAttackPose(); return false; }
            RestoreCorpseStepPose();
            RestoreCombatAttention();
            footwork?.Restore(); damagePose?.Restore(); bodyMotion?.Restore();
            AnimationClip clip = roomAttackPose switch
            {
                CombatRoomAttackPose.PointHero => roomPointHero,
                CombatRoomAttackPose.PointChest => roomPointChest,
                _ => roomLaugh
            };
            SampleNpcClip(clip, clip.length * roomAttackPoseProgress);
            handPose.SetGrip(false, 0f);
            handPose.SetGrip(true, 0f);
            SetRoomPointingWeight(roomAttackPose == CombatRoomAttackPose.Laugh ? 0f : 1f);
            visibleClip = clip.name;
            RememberNpcPresentedPose();
            return true;
        }

        private void SetRoomPointingWeight(float weight)
        {
            if (roomPointingRenderers == null) return;
            for (int i = 0; i < roomPointingRenderers.Length; i++)
            {
                SkinnedMeshRenderer surface = roomPointingRenderers[i];
                // A wound can replace a skin before the impact cancels this pose.
                if (surface != null && surface.sharedMesh == roomPointingMeshes[i])
                    surface.SetBlendShapeWeight(roomPointingShapes[i], weight * 100f);
            }
        }

        internal bool PrepareRoomHeadPainPresentation()
        {
            if (hero == null || hero.Registry == null) return false;
            if (roomHeadPain != null) return true;
            roomHeadPain = CombatRoomAttackAssetProvider.LoadHeroClip(CombatRoomAttackAssetProvider.HeadPainClip);
            hero.Registry.RegisterRuntimeAnimation(new Player3DAnimationBinding(roomHeadPain.name,
                "Combat", roomHeadPain, roomHeadPain.length, false));
            return true;
        }

        internal bool TryBeginRoomHeadPain()
        {
            if (roomHeadPainActive || hero == null || !isActiveAndEnabled || State.IsDefeated ||
                IsKnockedDown || IsRagdollActive || !BodyDamage.CanStand || !BodyDamage.CanUseLeftHand ||
                !PrepareRoomHeadPainPresentation() || !hero.CanAcquireClip(this)) return false;
            CancelPoseBlend();
            if (!hero.TryAcquireClip(this, roomHeadPain.name)) return false;
            roomHeadPainActive = true;
            roomHeadPainImpactCount = ReceivedImpactCount;
            roomHeadPainProgress = 0f;
            guardHeld = false;
            State.SetBlocking(false);
            supportGrip?.Restore(); weaponConstraint?.Restore();
            footwork?.Restore(); damagePose?.Restore(); bodyMotion?.Restore();
            hero.ClearCombatSupportGrip(this);
            hero.ClearCombatBodyMotion(this);
            hero.ClearCombatDamagePose(this);
            hero.ClearCombatFirearm(this);
            hero.ReleaseCarryPose(this);
            ReleaseCombatAttention();
            return SampleRoomHeadPain(0f);
        }

        internal bool SampleRoomHeadPain(float normalizedTime)
        {
            if (!roomHeadPainActive || float.IsNaN(normalizedTime) || float.IsInfinity(normalizedTime)) return false;
            roomHeadPainProgress = Mathf.Clamp01(normalizedTime);
            if (!PauseMenuController.IsAnyPaused && !GameTimeScaleRuntime.IsPaused) PresentRoomHeadPain();
            return roomHeadPainActive;
        }

        private bool PresentRoomHeadPain()
        {
            if (!roomHeadPainActive) return false;
            if (hero == null || !isActiveAndEnabled || State.IsDefeated || IsKnockedDown || IsRagdollActive ||
                !BodyDamage.CanStand || !BodyDamage.CanUseLeftHand || ReceivedImpactCount != roomHeadPainImpactCount ||
                !hero.OwnsClip(this))
            { EndRoomHeadPain(); return false; }
            RestoreCombatAttention();
            hero.SetOwnedClipLocomotion(this, false);
            hero.SampleOwnedClip(this, roomHeadPainProgress);
            motor?.SetOwnedMovementConstraint(this, 0f, 0f);
            handPose.SetGrip(false, weaponDropped ? 0f : 1f);
            handPose.SetGrip(true, 0f);
            visibleClip = roomHeadPain.name;
            hero.ReapplyLatePresentationPose();
            return true;
        }

        internal void EndRoomHeadPain()
        {
            if (!roomHeadPainActive) return;
            roomHeadPainActive = false;
            roomHeadPainProgress = 0f;
            if (hero != null && hero.OwnsClip(this)) hero.ReleaseOwnedClip(this);
            motor?.ReleaseMovementConstraint(this);
            visibleClip = null;
        }

        /// <summary>One nonphysical attack transaction; no palm sweep, wound, impulse or camera hit-stop.</summary>
        internal bool ReceiveRoomAttack(CombatActor source, int sequence, float damage = 18f)
        {
            if (hero == null || source == null || !source.IsRoom || State.IsDefeated || IsKnockedDown ||
                IsRagdollActive || !isActiveAndEnabled || float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0f ||
                lastRoomDamageSource == source && lastRoomDamageSequence == sequence) return false;
            MeleePhase before = State.Phase;
            float healthBefore = State.Health;
            MeleeHitLocation location = new MeleeHitLocation(MeleeBodyRegion.Head, MeleeHitSide.Front);
            MeleeHitResult result = State.ReceiveUnarmedHit(damage, 0f, false, 0f, location);
            if (result == MeleeHitResult.Ignored) return false;
            lastRoomDamageSource = source;
            lastRoomDamageSequence = sequence;
            reaction = null; reactionClock = 0f;
            collectSweep = sweepValid = false;
            CancelPendingKick("room_attack");
            CancelInterruptedShoveContact();
            Vector3 point = hero.Registry.Anchors.Head.position;
            Vector3 direction = transform.position - source.transform.position;
            direction = direction.sqrMagnitude > .0001f ? direction.normalized : -transform.forward;
            PublishImpact(new CombatImpact(source, this, sequence, point, -direction, direction,
                healthBefore, State.Health, result, location, part: Player3DAnatomicalPart.Head,
                kind: CombatImpactKind.Unarmed, primaryResponse: false, headFeedback: false,
                bodyRegion: BodyDamageRegion.Head), before);
            if (State.IsDefeated) BeginDefeat(direction, point);
            return true;
        }

        private void ResetRoomSpecialPresentation()
        {
            EndRoomAttackPose();
            EndRoomHeadPain();
        }

        private void ResetRoomAttackDamageReceipt()
        {
            lastRoomDamageSource = null;
            lastRoomDamageSequence = int.MinValue;
        }

        private void SetRoomLaughFrozen(bool frozen)
        {
            if (!roomLaughStarted || roomLaughVoice == null) return;
            if (frozen) roomLaughVoice.Pause();
            else roomLaughVoice.UnPause();
        }
    }
}
