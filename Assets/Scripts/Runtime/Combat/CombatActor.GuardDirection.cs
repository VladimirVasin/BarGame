using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        // The same horizontal front sector protects every blockable contact.
        // Elevation alone never turns a frontal hook into a rear strike.
        internal bool FacingIncomingAttack(CombatActor source, Vector3 travel)
        {
            Vector3 origin = source != null
                ? source.IsCrane ? source.CraneHookFlightOrigin : source.BodyWorldPosition
                : BodyWorldPosition - travel;
            Vector3 incoming = Vector3.ProjectOnPlane(origin - BodyWorldPosition, Vector3.up);
            if (incoming.sqrMagnitude < .0001f)
                incoming = Vector3.ProjectOnPlane(-travel, Vector3.up);
            // A precisely vertical contact has no horizontal rear hemisphere.
            return incoming.sqrMagnitude < .0001f ||
                Vector3.Dot(Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized, incoming.normalized) >= .35f;
        }

        private bool CanBlockIncoming(CombatActor source, Vector3 travel) =>
            IsHero && WeaponId == CombatWeaponId.Crowbar && !weaponDropped && !IsSwitchingWeapon &&
            State.IsBlocking && HasAttackBalance && HasTwoHandSupport && FacingIncomingAttack(source, travel);

        private MeleeHitResult ReceiveTimedParry(CombatActor source, int sequence, Vector3 point,
            Vector3 normal, Vector3 direction, float power, MeleeHitLocation location = default,
            Player3DAnatomicalPart part = Player3DAnatomicalPart.Torso, Vector3 localPoint = default,
            float weaponSpeed = 0f, CombatImpactKind kind = CombatImpactKind.Weapon)
        {
            MeleePhase previous = State.Phase;
            MeleeHitResult result = State.ReceiveWeaponObstacle(source.State.AttackBlockCost, power);
            if (result == MeleeHitResult.Ignored) return result;
            reaction = guardImpact; reactionClock = 0f;
            PublishImpact(new CombatImpact(source, this, sequence, point, normal, direction,
                State.Health, State.Health, result, location, power, part, localPoint, weaponSpeed,
                impulse: CombatImpactMotion.ResolveImpulse(direction, power, weaponSpeed, result), kind: kind), previous);
            if (kind != CombatImpactKind.Weapon) RetroAudio.PlayAt(RetroSfxId.SpadeGlance, point, .8f);
            Present();
            return result;
        }

        // Parrying already ended the rules' active window. Stop physical ram motion
        // without requesting a second obstacle result that would erase that outcome.
        private void StopParriedZombieAttack()
        {
            zombieLoad?.EndRam();
            collectSweep = false;
            zombieContactPoseValid = false;
        }

        // Called only once, at the end of the physical return and regeneration.
        // The live actor, its ceiling anchors and encounter identity stay put.
        private void RestoreCraneAfterRegeneration()
        {
            State.Reset();
            ResetDamage();
            reaction = null;
            reactionClock = 0f;
            roundEnded = false;
            sweepValid = collectSweep = collectShove = collectKick = false;
            ResetWeaponContacts();
        }
    }
}
