using UnityEngine;

namespace BarPromenade
{
    public sealed partial class PlayerMotor
    {
        /// <summary>Moves an owning action through the same ground bounds and capsule as ordinary locomotion.</summary>
        public Vector3 ApplyOwnedDisplacement(object owner, Vector3 displacement)
        {
            if (owner == null || !ReferenceEquals(owner, movementConstraintOwner) ||
                ownedMoveScale > 0f || controller == null || !controller.enabled || !InputEnabled ||
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
