using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private bool roundEnded;
        private bool winnerPresentationReleased;
        private float defeatClock;
        private Vector3 defeatDirection, defeatPoint;
        private Rigidbody weaponBody;
        private CombatHeldWeaponPhysics heldWeaponPhysics;
        private Transform weaponGrip;
        private Vector3 weaponPosition, weaponScale;
        private Quaternion weaponRotation;
        private bool weaponDropped;

        public CombatRagdoll Ragdoll { get; private set; }
        public bool IsRagdollActive => Ragdoll != null && Ragdoll.IsActive;
        public string ActiveClipName => visibleClip;
        public bool IsWeaponDropped => weaponDropped;
        /// <summary>The standing winner's bar waits in his closed left hand while the right one is busy.</summary>
        public bool IsWeaponInLeftHand { get; private set; }

        private bool CanAttackFinishedBody => hero != null && !IsFirearm && !State.IsDefeated &&
            contactTarget != null && contactTarget.State.IsDefeated;

        private void ResumeFinishedBodyAttack()
        {
            if (!CanAttackFinishedBody) return;
            ReturnWeaponToRightHand();
            winnerPresentationReleased = false;
        }

        private void BeginDefeat(Vector3 direction, Vector3 point)
        {
            roundEnded = true;
            EndPistolAction();
            CancelPendingKick("round_ended");
            defeatClock = 0f;
            defeatDirection = direction.sqrMagnitude > .001f ? direction.normalized : -transform.forward;
            defeatPoint = point;
            if (IsKnockedDown) PromoteKnockdownToDefeat();
            reaction = null;
            sweepValid = false;
        }

        private void BeginProjectileDefeat(CombatImpact impact)
        {
            BeginDefeat(impact.Direction, impact.Point);
            // A lying or rising target already owns the same rig in physics.
            // BeginDefeat promotes that fall without replaying a standing clip.
            if (IsRagdollActive) return;
            hero?.SetOwnedPresentationFrozen(this, false);
            Vector3 carry = motor != null ? motor.PlanarVelocity : locomotionVelocity;
            carry += ImpactMotion?.Velocity ?? Vector3.zero;
            Vector3 angular = ImpactMotion?.AngularVelocity ?? Vector3.zero;
            if (!Ragdoll.BeginTerminal(carry, angular))
                throw new InvalidOperationException("The projectile-defeated combat rig could not hand its live pose to physics.");
            ForgetPistolAimPose();
            supportGrip?.Forget();
            weaponConstraint?.Forget();
            footwork?.Forget();
            damagePose?.ForgetBase();
            bodyMotion?.Forget();
            ImpactMotion?.Forget();
            CancelPoseBlend();
            DropWeapon();
        }

        internal void AdvanceRoundEnd(float seconds)
        {
            roundEnded = true;
            if (CanAttackFinishedBody)
            {
                State.SetBlocking(false);
                guardHeld = false;
                if (State.IsCharging || State.IsAttacking || State.HasBufferedAttack || State.HasBufferedCharge ||
                    IsKnockedDown || IsRagdollActive || State.IsKnockedDown)
                {
                    ResumeFinishedBodyAttack();
                    AdvanceSimulation(seconds);
                    Present();
                    return;
                }
                State.Advance(seconds);
                if (!winnerPresentationReleased)
                {
                    ReleaseStandingPresentation();
                    winnerPresentationReleased = true;
                }
                return;
            }
            CancelPendingKick("round_ended");
            State.CancelCharge();
            State.SetBlocking(false);
            if (IsFirearm && hero != null && !State.IsDefeated)
            {
                // The duel result stops hostile actions, not the winner's firearm.
                winnerPresentationReleased = false;
                collectSweep = collectShove = collectKick = sweepValid = false;
                AdvancePistol(seconds);
                if (AdvanceKnockdown(seconds)) return;
                AdvanceVisualClock(seconds);
                AdvanceImpactMotion(seconds);
                State.Advance(seconds);
                simulationPosePending = true;
                CompleteSimulationPose(seconds);
                Present();
                return;
            }
            EndPistolAction();
            if (State.IsDefeated) { AdvanceDefeat(seconds); return; }
            if (winnerPresentationReleased) { State.Advance(seconds); return; }
            // The winner finishes the visible swing without another damage window.
            AdvanceVisualClock(seconds);
            State.Advance(seconds);
            if (hero != null && State.Phase == MeleePhase.Ready)
            {
                // Hand the whole rig back to ordinary locomotion once. A Rest
                // clip with combat footwork would keep the winner shuffling.
                ReleaseStandingPresentation();
                winnerPresentationReleased = true;
                return;
            }
            footwork?.Advance(seconds, State);
            Present();
        }

        private void AdvanceDefeat(float seconds)
        {
            if (IsRagdollActive) return;
            AdvanceVisualClock(seconds);
            footwork?.Advance(seconds, State);
            defeatClock = Mathf.Min(defeatClock + seconds, CombatAssetProvider.DefeatHandoffSeconds);
            Present();
            if (defeatClock + .000001f < CombatAssetProvider.DefeatHandoffSeconds) return;
            supportGrip?.Forget();
            footwork?.Forget();
            if (hero == null) { damagePose?.ForgetBase(); bodyMotion?.Forget(); }
            // Begin takes the same bones in their current impact pose. Ending the
            // owned clip first would replace that pose with ordinary locomotion.
            if (!Ragdoll.Begin(defeatDirection, defeatPoint))
                throw new InvalidOperationException("The defeated combat rig could not hand its pose to physics.");
            ForgetPistolAimPose();
            weaponConstraint?.Forget();
            CancelPoseBlend();
            DropWeapon();
        }

        private void ResetDefeat()
        {
            roundEnded = false;
            winnerPresentationReleased = false;
            defeatClock = 0f;
            defeatDirection = defeatPoint = Vector3.zero;
            poseBlendAfterFrame = Time.frameCount + 1;
        }

        private void PrepareWeaponPhysics()
        {
            weaponGrip = Weapon.transform.parent;
            weaponPosition = Weapon.transform.localPosition;
            weaponRotation = Weapon.transform.localRotation;
            weaponScale = Weapon.transform.localScale;
            if (IsShotgun) CombatShotgunAssetProvider.AddDropColliders(Weapon);
            else if (IsPistol) CombatPistolAssetProvider.AddDropColliders(Weapon);
            weaponBody = Weapon.AddComponent<Rigidbody>();
            weaponBody.isKinematic = true;
            weaponBody.useGravity = false;
            weaponBody.detectCollisions = false;
            weaponBody.mass = IsShotgun ? 3.2f : IsPistol ? .8f : 1.1f;
            weaponBody.linearDamping = .4f;
            weaponBody.angularDamping = .7f;
            weaponBody.maxAngularVelocity = 10f;
            weaponBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            PrepareHeldWeaponPhysics();
        }

        private void PrepareHeldWeaponPhysics()
        {
            if (IsFirearm || heldWeaponPhysics != null || Weapon == null) return;
            Transform forearm = CityPedestrianHandProps.FindSocket(DamageRigRoot, "forearm.R");
            Rigidbody armBody = forearm != null ? forearm.GetComponent<Rigidbody>() : null;
            if (armBody == null) throw new InvalidOperationException("Held crowbar collision requires the right forearm rigidbody.");
            heldWeaponPhysics = gameObject.AddComponent<CombatHeldWeaponPhysics>();
            heldWeaponPhysics.Initialize(Weapon.transform, weaponBody, armBody,
                Ragdoll.PhysicsController.AnatomicalColliders, handPose);
        }

        internal void EnableHeldWeaponPhysics()
        {
            if (weaponDropped) return;
            PrepareHeldWeaponPhysics();
            heldWeaponPhysics?.EnableHeld();
        }

        internal void DisableHeldWeaponPhysics() => heldWeaponPhysics?.DisableHeld();

        internal void DisposeHeldWeaponPhysics()
        {
            if (heldWeaponPhysics == null) return;
            heldWeaponPhysics.Dispose();
            Destroy(heldWeaponPhysics);
            heldWeaponPhysics = null;
        }

        private void DropWeapon()
        {
            ReleaseWeapon(defeatDirection * .8f + Vector3.up * .15f,
                Vector3.Cross(Vector3.up, defeatDirection) * 2f);
        }

        private void ReleaseWeapon(Vector3 linearVelocity, Vector3 angularVelocity)
        {
            if (weaponDropped || Weapon == null) return;
            EndPistolAction();
            weaponConstraint?.EndRecoveryContact();
            handPose.SetGrip(false, 0f);
            handPose.SetGrip(true, 0f);
            Weapon.transform.SetParent(transform.parent, true);
            PrepareHeldWeaponPhysics();
            heldWeaponPhysics?.EnableDropped();
            if (IsFirearm) IgnorePistolBodyCollisions(true);
            weaponBody.detectCollisions = true;
            weaponBody.useGravity = true;
            weaponBody.interpolation = RigidbodyInterpolation.Interpolate;
            weaponBody.isKinematic = false;
            weaponBody.linearVelocity = linearVelocity;
            weaponBody.angularVelocity = angularVelocity;
            weaponDropped = true;
            if (hero != null)
            {
                var pickup = Weapon.GetComponent<CombatDroppedWeaponPickup>();
                if (pickup == null) pickup = Weapon.AddComponent<CombatDroppedWeaponPickup>();
                pickup.Initialize(this);
            }
        }

        private void RestoreWeapon()
        {
            Weapon?.GetComponent<CombatDroppedWeaponPickup>()?.CancelPickup();
            heldWeaponPhysics?.ResetWeapon();
            if (!weaponDropped || Weapon == null) return;
            if (IsFirearm) IgnorePistolBodyCollisions(false);
            weaponBody.linearVelocity = Vector3.zero;
            weaponBody.angularVelocity = Vector3.zero;
            weaponBody.isKinematic = true;
            weaponBody.useGravity = false;
            weaponBody.detectCollisions = false;
            weaponBody.interpolation = RigidbodyInterpolation.None;
            if (weaponGrip == null) { Destroy(Weapon); weaponDropped = false; return; }
            Weapon.transform.SetParent(weaponGrip, false);
            Weapon.transform.localPosition = weaponPosition;
            Weapon.transform.localRotation = weaponRotation;
            Weapon.transform.localScale = weaponScale;
            weaponDropped = false;
        }

        /// <summary>
        /// Only the finished round's standing winner: once combat has let the
        /// rig go, the bar moves to the closed left hand and the right opens
        /// for a contextual action. A later corpse swing first returns it to the right hand.
        /// </summary>
        internal bool TryHoldWeaponInLeftHand()
        {
            if (IsFirearm) return false;
            if (IsWeaponInLeftHand) return true;
            if (Weapon == null || hero == null || weaponDropped || IsRagdollActive || !winnerPresentationReleased) return false;
            Transform leftGrip = hero.Registry.Anchors.LeftGrip;
            if (leftGrip == null) return false;
            CombatAssetProvider.PlaceCrowbar(Weapon, leftGrip, handPose, true);
            handPose.SetGrip(false, 0f);
            handPose.SetGrip(true, 1f);
            IsWeaponInLeftHand = true;
            return true;
        }

        /// <summary>Clip sampling can loosen the fingers; the action re-asserts the hold every presentation frame.</summary>
        internal void ReassertLeftHandHold()
        {
            if (!IsWeaponInLeftHand || handPose == null) return;
            handPose.SetGrip(true, 1f);
            handPose.SetGrip(false, 0f);
        }

        internal void ReturnWeaponToRightHand()
        {
            if (!IsWeaponInLeftHand || Weapon == null || weaponGrip == null) return;
            // Scene teardown is already deactivating the hierarchy; Unity forbids reparenting inside it.
            if (!gameObject.activeInHierarchy) return;
            Weapon.transform.SetParent(weaponGrip, false);
            Weapon.transform.localPosition = weaponPosition;
            Weapon.transform.localRotation = weaponRotation;
            Weapon.transform.localScale = weaponScale;
            handPose.SetGrip(true, 0f);
            handPose.SetGrip(false, 1f);
            IsWeaponInLeftHand = false;
        }
    }
}
