using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private bool roundEnded;
        private float defeatClock;
        private Vector3 defeatDirection, defeatPoint;
        private Rigidbody weaponBody;
        private CapsuleCollider weaponCollider;
        private Transform weaponGrip;
        private Vector3 weaponPosition, weaponScale;
        private Quaternion weaponRotation;
        private bool weaponDropped;

        public CombatRagdoll Ragdoll { get; private set; }
        public bool IsRagdollActive => Ragdoll != null && Ragdoll.IsActive;
        public string ActiveClipName => visibleClip;
        public bool IsWeaponDropped => weaponDropped;

        private void BeginDefeat(Vector3 direction, Vector3 point)
        {
            roundEnded = true;
            defeatClock = 0f;
            defeatDirection = direction.sqrMagnitude > .001f ? direction.normalized : -transform.forward;
            defeatPoint = point;
            reaction = null;
            sweepValid = false;
        }

        internal void AdvanceRoundEnd(float seconds)
        {
            roundEnded = true;
            State.CancelCharge();
            State.SetBlocking(false);
            if (State.IsDefeated) { AdvanceDefeat(seconds); return; }
            // The winner finishes the visible swing without another damage window.
            AdvanceVisualClock(seconds);
            State.Advance(seconds);
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
            CancelPoseBlend();
            DropWeapon();
        }

        private void ResetDefeat()
        {
            roundEnded = false;
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
            weaponBody = Weapon.AddComponent<Rigidbody>();
            weaponBody.isKinematic = true;
            weaponBody.useGravity = false;
            weaponBody.detectCollisions = false;
            weaponBody.mass = 1.1f;
            weaponBody.linearDamping = .4f;
            weaponBody.angularDamping = .7f;
            weaponBody.maxAngularVelocity = 10f;
            weaponBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            weaponCollider = Weapon.AddComponent<CapsuleCollider>();
            weaponCollider.enabled = false;
            weaponCollider.direction = 1;
            weaponCollider.center = new Vector3(0f, .24f, .04f);
            weaponCollider.height = .80f;
            weaponCollider.radius = .04f;
        }

        private void DropWeapon()
        {
            if (weaponDropped || Weapon == null) return;
            handPose.SetGrip(false, 0f);
            handPose.SetGrip(true, 0f);
            Weapon.transform.SetParent(transform.parent, true);
            weaponCollider.enabled = true;
            foreach (Collider owned in GetComponentsInChildren<Collider>(true))
                Physics.IgnoreCollision(weaponCollider, owned, true);
            weaponBody.detectCollisions = true;
            weaponBody.useGravity = true;
            weaponBody.interpolation = RigidbodyInterpolation.Interpolate;
            weaponBody.isKinematic = false;
            weaponBody.linearVelocity = defeatDirection * .8f + Vector3.up * .15f;
            weaponBody.angularVelocity = Vector3.Cross(Vector3.up, defeatDirection) * 2f;
            weaponDropped = true;
        }

        private void RestoreWeapon()
        {
            if (!weaponDropped || Weapon == null) return;
            weaponBody.linearVelocity = Vector3.zero;
            weaponBody.angularVelocity = Vector3.zero;
            weaponBody.isKinematic = true;
            weaponBody.useGravity = false;
            weaponBody.detectCollisions = false;
            weaponBody.interpolation = RigidbodyInterpolation.None;
            weaponCollider.enabled = false;
            if (weaponGrip == null) { Destroy(Weapon); weaponDropped = false; return; }
            Weapon.transform.SetParent(weaponGrip, false);
            Weapon.transform.localPosition = weaponPosition;
            Weapon.transform.localRotation = weaponRotation;
            Weapon.transform.localScale = weaponScale;
            weaponDropped = false;
        }
    }
}
