using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatRagdoll
    {
        private bool restraintSuspension, restraintLandingPending;
        private float restraintLandingSpeed;

        internal bool BeginRestraintSuspension(Player3DAnatomicalPart part, Vector3 localPoint, Vector3 target)
        {
            if (!IsActive || !recoverable || physicsController == null) return false;
            if ((IsRecovering || physicsController.IsFrozen) && !ResumeHeldSimulation()) return false;
            if (!physicsController.BeginRestraintDrive(part, localPoint, target)) return false;
            restraintSuspension = true;
            restraintLandingPending = false;
            restraintLandingSpeed = 0f;
            IsSettled = false;
            quietSeconds = 0f;
            return true;
        }

        internal void UpdateRestraintSuspension(Vector3 target) => physicsController?.UpdateRestraintDrive(target);

        internal void EndRestraintSuspension()
        {
            if (!restraintSuspension) return;
            restraintSuspension = false;
            physicsController?.EndRestraintDrive();
            // Release preserves every body's velocity. One subsequent anatomical
            // landing consumes this episode even when it is too soft to hurt.
            restraintLandingPending = IsActive && recoverable;
            restraintLandingSpeed = DownwardCentralSpeed();
            IsSettled = false;
            quietSeconds = 0f;
        }

        private float DownwardCentralSpeed()
        {
            if (physicsController == null) return 0f;
            return Mathf.Max(0f, Mathf.Max(-physicsController.PelvisBody.linearVelocity.y,
                -physicsController.ChestBody.linearVelocity.y));
        }

        private void AdvanceRestraintPhysics(float seconds)
        {
            if (restraintSuspension) physicsController.AdvanceRestraintDrive(seconds);
            if (restraintLandingPending) restraintLandingSpeed = Mathf.Max(restraintLandingSpeed, DownwardCentralSpeed());
        }

        internal void RegisterRestraintLanding(Collision collision)
        {
            if (!restraintLandingPending || restraintSuspension || !IsActive || IsRecovering || hitStopFrozen ||
                Time.timeScale <= 0f || PauseMenuController.IsAnyPaused || collision == null ||
                !IsStaticSupportSurface(collision.collider)) return;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                if (contact.thisCollider == null || !physicsController.IsCombatAnatomicalCollider(contact.thisCollider) ||
                    contact.normal.y <= .65f || contact.separation > .02f) continue;
                float speed = Mathf.Max(restraintLandingSpeed,
                    Mathf.Max(0f, Vector3.Dot(collision.relativeVelocity, -contact.normal)));
                restraintLandingPending = false;
                restraintLandingSpeed = 0f;
                combatActor?.ReceiveRestraintLanding(speed, contact.point, contact.normal.normalized);
                return;
            }
        }

        private void ResetRestraintPhysics()
        {
            restraintSuspension = restraintLandingPending = false;
            restraintLandingSpeed = 0f;
            physicsController?.EndRestraintDrive();
        }
    }
}
