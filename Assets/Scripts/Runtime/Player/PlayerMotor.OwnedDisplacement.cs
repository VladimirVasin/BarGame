using UnityEngine;

namespace BarPromenade
{
    public sealed partial class PlayerMotor
    {
        /// <summary>Moves an owning action through the same ground bounds and capsule as ordinary locomotion.</summary>
        public Vector3 ApplyOwnedDisplacement(object owner, Vector3 displacement)
        {
            if (ownedMoveScale > 0f) return Vector3.zero;
            return ApplyOwnedImpulseDisplacement(owner, displacement);
        }

        /// <summary>Clocked physical momentum is independent of the allowance for voluntary travel.</summary>
        public Vector3 ApplyOwnedImpulseDisplacement(object owner, Vector3 displacement)
        {
            if (owner == null || !ReferenceEquals(owner, movementConstraintOwner) ||
                controller == null || !controller.enabled || !InputEnabled ||
                !GameInput.CanRead(GameInputContext.Movement) || !IsFinite(displacement)) return Vector3.zero;
            displacement.y = 0f;
            Vector3 refusal = Vector3.zero;
            CollisionFlags flags = CollisionFlags.None;
            Vector3 moved = MoveConstrainedDrift(displacement, ref refusal, ref flags);
            lastContact = PlayerMotorContactSample.From(flags, sideHitThisMove, sideHitNormal, sideHitPoint, refusal);
            return moved;
        }
    }
}
