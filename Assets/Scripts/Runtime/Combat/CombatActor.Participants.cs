using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        internal IReadOnlyList<CombatActor> CombatParticipants { get; private set; }
        internal int ContactGeneration { get; private set; }

        // The root retains this roster for the round. Camera/pose focus may
        // change freely without narrowing the physical contact participants.
        internal void SetCombatParticipants(IReadOnlyList<CombatActor> participants)
        {
            CombatParticipants = participants;
            weaponConstraint?.SetCombatParticipants(participants);
            heldWeaponPhysics?.SetCombatParticipants(participants);
        }

        internal void InvalidateCombatContacts()
        {
            ContactGeneration = unchecked(ContactGeneration + 1);
            frozenWeaponValid = frozenWeaponActive = sweepValid = collectSweep = false;
        }

        private int ContactParticipantCount => CombatParticipants?.Count ?? (contactTarget != null ? 1 : 0);
        private CombatActor ContactParticipantAt(int index) => CombatParticipants != null ? CombatParticipants[index] : contactTarget;
        internal static bool HasPhysicalBody(CombatActor candidate) => candidate != null && candidate.isActiveAndEnabled &&
            candidate.gameObject.activeInHierarchy && candidate.Hurtboxes != null;
        internal bool CanDamageParticipant(CombatActor candidate) => candidate != this && HasPhysicalBody(candidate) &&
            IsHero != candidate.IsHero;

        private bool IsParticipantShape(Transform shape)
        {
            if (shape.IsChildOf(transform)) return true;
            for (int i = 0; i < ContactParticipantCount; i++)
            {
                CombatActor candidate = ContactParticipantAt(i);
                if (HasPhysicalBody(candidate) && shape.IsChildOf(candidate.transform)) return true;
            }
            return false;
        }
    }
}
