using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        public const int RestraintEscapePresses = 6;
        public const float RestraintLandingSafeSpeed = 4.5f;
        private CombatActor restraintSource;
        private CombatTestRoot restraintSceneOwner;
        private bool restraintSceneOwnerResolved;
        private PlayerInteractor restraintInteractor;
        private Transform restraintBone;
        private Player3DAnatomicalPart restraintPart;
        private Vector3 restraintLocalPoint, restraintDragVelocity;
        private bool restraintActive, restraintPressArmed;
        private int restraintPresses, restraintInputFrame = -1;
        private float restraintSeconds;

        public bool IsRestrained => restraintActive;
        public bool IsRestraintAirborne => restraintActive && IsRagdollActive;
        public float RestraintEscapeProgress => Mathf.Clamp01(restraintPresses / (float)RestraintEscapePresses);
        public Vector3 RestraintAnchor => restraintBone != null
            ? restraintBone.TransformPoint(restraintLocalPoint) : BodyWorldPosition;
        public int RestraintFallDamageCount { get; private set; }
        public float LastRestraintFallDamage { get; private set; }

        /// <summary>The resolved anatomical contact owns the attachment; no second hit or HP transaction occurs.</summary>
        public bool TryBeginCraneRestraint(CombatActor source, Player3DAnatomicalPart part,
            Vector3 localPoint, Vector3 worldPoint)
        {
            if (source == null || !source.IsCrane || hero == null || restraintActive || State.IsDefeated ||
                !isActiveAndEnabled || (interaction != null && interaction.IsActive) || Ragdoll == null ||
                !FiniteRestraintVector(localPoint) || !FiniteRestraintVector(worldPoint) ||
                !Ragdoll.PhysicsController.TryGetCombatBone(part, out Transform bone)) return false;
            if (IsRagdollActive && !IsKnockedDown) return false;
            if (!IsRagdollActive && !hero.TryAcquireClip(this, hit.name)) return false;
            if (!IsRagdollActive && motor != null && !motor.SetOwnedMovementConstraint(this, 0f, 0f))
            {
                hero.ReleaseOwnedClip(this);
                return false;
            }
            CancelWeaponCarry(retainPose: true);
            EndPistolAction();
            CancelPendingKick("restrained");
            CancelInterruptedShoveContact();
            State.CancelAction();
            guardHeld = false;
            reaction = null;
            collectSweep = sweepValid = collectShove = collectKick = false;
            ImpactMotion?.Forget();
            footwork?.Forget();
            supportGrip?.SetTarget(false, false);
            supportGrip?.AllowRegrip(false);
            supportGrip?.SetRecoveryOwned(true);
            if (!IsRagdollActive) motor?.SetOwnedMovementConstraint(this, 0f, 0f);
            restraintSource = source;
            restraintBone = bone;
            restraintPart = part;
            restraintLocalPoint = localPoint;
            restraintDragVelocity = Vector3.zero;
            restraintSeconds = 0f;
            restraintPresses = 0;
            restraintInputFrame = Time.frameCount;
            restraintPressArmed = !GameInput.IsHeld(GameInputAction.Interact, GameInputContext.Gameplay);
            restraintActive = true;
            if (!restraintSceneOwnerResolved)
            {
                restraintSceneOwnerResolved = true;
                restraintSceneOwner = source.GetComponentInParent<CombatTestRoot>();
                if (restraintSceneOwner == null) restraintSceneOwner = FindAnyObjectByType<CombatTestRoot>();
            }
            restraintSceneOwner?.SetOpponentFocus(false);
            if (restraintInteractor == null) restraintInteractor = GetComponent<PlayerInteractor>();
            restraintInteractor?.SetOwnedInteractKeyClaim(this, true);
            if (IsRagdollActive) Ragdoll.BeginRestraintSuspension(part, localPoint, source.CraneHookPullOrigin);
            PresentRestraint();
            return true;
        }

        public bool TryGetCraneRestraintAnchor(CombatActor source, out Pose anchor)
        {
            anchor = default;
            if (!restraintActive || source != restraintSource || restraintBone == null) return false;
            anchor = new Pose(RestraintAnchor, restraintBone.rotation);
            return true;
        }

        internal void AdvanceRestraint(float seconds)
        {
            if (!restraintActive) return;
            if (restraintSource == null || !restraintSource.isActiveAndEnabled || State.IsDefeated || restraintBone == null)
            { CancelRestraint(); return; }
            if (seconds <= 0f || presentationFrozen || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused) return;
            restraintSeconds += seconds;
            ReadRestraintEscape();
            if (!restraintActive) return;
            Vector3 target = restraintSource.CraneHookPullOrigin;
            Vector3 delta = target - RestraintAnchor;
            if (!FiniteRestraintVector(target)) { CancelRestraint(); return; }
            if (!IsRagdollActive)
            {
                // The capsule owns grounded dragging. Upward tension hands the current
                // visible pose to the existing ragdoll before any bone becomes dynamic.
                bool lifting = restraintSeconds >= .16f && delta.y > .75f &&
                    delta.y > Vector3.ProjectOnPlane(delta, Vector3.up).magnitude * .30f;
                bool lostGround = motor != null && !motor.IsGrounded && restraintSeconds >= .16f;
                if ((lifting || lostGround) && BeginRestraintPhysics(target)) return;
                Vector3 planar = Vector3.ProjectOnPlane(delta, Vector3.up);
                Vector3 wanted = planar.sqrMagnitude > .01f ? planar.normalized * Mathf.Min(2.2f, planar.magnitude) : Vector3.zero;
                Vector3 displacement = Vector3.MoveTowards(restraintDragVelocity, wanted, seconds * 7f) * seconds;
                Vector3 moved = motor != null ? motor.ApplyOwnedForcedDisplacement(this, displacement) : Vector3.zero;
                restraintDragVelocity = moved / seconds;
                motor?.SetOwnedMovementConstraint(this, 0f, 0f);
            }
            else Ragdoll.UpdateRestraintSuspension(target);
        }

        private bool BeginRestraintPhysics(Vector3 target)
        {
            PresentRestraint();
            var impact = new CombatImpact(restraintSource, this, restraintSource.State.AttackSequence,
                RestraintAnchor, Vector3.up, (target - RestraintAnchor).normalized,
                State.Health, State.Health, MeleeHitResult.Hit, part: restraintPart,
                localPoint: restraintLocalPoint, kind: CombatImpactKind.Hook, primaryResponse: false,
                headFeedback: false);
            if (!IsKnockedDown && !TryBeginKnockdown(impact, restraintDragVelocity, Vector3.zero)) return false;
            return Ragdoll.BeginRestraintSuspension(restraintPart, restraintLocalPoint, target);
        }

        private void ReadRestraintEscape()
        {
            if (restraintInputFrame == Time.frameCount || !GameInput.CanRead(GameInputContext.Gameplay)) return;
            restraintInputFrame = Time.frameCount;
            if (!GameInput.IsHeld(GameInputAction.Interact, GameInputContext.Gameplay)) restraintPressArmed = true;
            if (!restraintPressArmed || !GameInput.WasPressed(GameInputAction.Interact, GameInputContext.Gameplay)) return;
            restraintPressArmed = false;
            RegisterRestraintEscapePress();
        }

        /// <summary>One already-resolved new press, shared by input and deterministic diagnostics.</summary>
        public bool RegisterRestraintEscapePress()
        {
            if (!restraintActive || presentationFrozen || PauseMenuController.IsAnyPaused ||
                GameTimeScaleRuntime.IsPaused || State.IsDefeated) return false;
            restraintPresses = Mathf.Min(RestraintEscapePresses, restraintPresses + 1);
            if (restraintPresses == RestraintEscapePresses)
            {
                CombatActor source = restraintSource;
                source?.ReleaseCraneVictimEarly();
                if (restraintActive) EndCraneRestraint(source);
            }
            return true;
        }

        internal bool PresentRestraint()
        {
            if (!restraintActive) return false;
            if (IsRagdollActive) return true;
            RestoreCombatAttention();
            if (!hero.OwnsClip(this) && !hero.TryAcquireClip(this, hit.name)) return true;
            float struggle = Mathf.Sin(restraintSeconds * 12f);
            hero.SetOwnedClipLocomotion(this, false);
            hero.SampleOwnedClipBlend(this, guardBreak.name, .18f + .10f * struggle, .34f + .065f * struggle);
            damagePose?.SetRestraintContact(restraintPart, RestraintAnchor, restraintSeconds);
            hero.SetCombatDamagePose(this, damagePose, 1f);
            visibleClip = hit.name;
            handPose?.SetGrip(false, 0f);
            handPose?.SetGrip(true, 0f);
            hero.ReapplyLatePresentationPose();
            return true;
        }

        public void EndCraneRestraint(CombatActor source)
        {
            if (!restraintActive || source != restraintSource) return;
            ReleaseRestraint();
        }

        internal void CancelRestraint()
        {
            if (!restraintActive) return;
            CombatActor source = restraintSource;
            ReleaseRestraint();
            source?.ReleaseCraneVictimEarly();
        }

        internal void ResetRestraintDiagnostics()
        {
            restraintPresses = 0;
            RestraintFallDamageCount = 0;
            LastRestraintFallDamage = 0f;
        }

        private void ReleaseRestraint()
        {
            damagePose?.ClearRestraintContact();
            restraintActive = false;
            restraintSource = null;
            restraintBone = null;
            restraintPressArmed = false;
            restraintInteractor?.SetOwnedInteractKeyClaim(this, false);
            Ragdoll?.EndRestraintSuspension();
            motor?.ReleaseMovementConstraint(this);
            if (!IsKnockedDown) supportGrip?.SetRecoveryOwned(false);
            supportGrip?.AllowRegrip(!weaponDropped);
            if (!IsRagdollActive && hero != null && hero.OwnsClip(this))
            {
                hero.RememberOwnedRecoveryPose(this, poseClock);
                hero.ReleaseOwnedClip(this);
                visibleClip = null;
            }
            restraintDragVelocity = Vector3.zero;
        }

        internal void ReceiveRestraintLanding(float speed, Vector3 point, Vector3 normal)
        {
            if (!IsHero || State.IsDefeated || !float.IsFinite(speed) || speed <= RestraintLandingSafeSpeed) return;
            float before = State.Health;
            float excess = speed - RestraintLandingSafeSpeed;
            MeleePhase phase = State.Phase;
            MeleeHitResult result = State.ReceiveEnvironmentDamage(Mathf.Min(45f, excess * excess * 3f));
            if (result == MeleeHitResult.Ignored) return;
            LastRestraintFallDamage = Mathf.Max(0f, before - State.Health);
            if (LastRestraintFallDamage > 0f) RestraintFallDamageCount++;
            PublishImpact(new CombatImpact(null, this, RestraintFallDamageCount, point, normal, -normal,
                before, State.Health, result, part: Player3DAnatomicalPart.Pelvis,
                kind: CombatImpactKind.Fall, primaryResponse: false, headFeedback: false), phase);
            if (State.IsDefeated) BeginDefeat(-normal, point);
        }

        private static bool FiniteRestraintVector(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
