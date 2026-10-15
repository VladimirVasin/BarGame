using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A released board or fish reports only a few real impacts; it owns no audio source.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatZombieMaterialContact : MonoBehaviour
    {
        public const int MaximumContacts = 4;
        private CombatZombieAudio owner;
        private bool wood;
        public int AcceptedContactCount { get; private set; }

        public void Initialize(CombatZombieAudio audio, bool wooden)
        {
            owner = audio != null ? audio : throw new ArgumentNullException(nameof(audio));
            wood = wooden;
            ResetContacts();
        }

        public void ResetContacts() => AcceptedContactCount = 0;

        private void OnCollisionEnter(Collision collision)
        {
            if (owner == null || AcceptedContactCount >= MaximumContacts ||
                collision == null || collision.contactCount == 0) return;
            Vector3 velocity = collision.relativeVelocity;
            if (velocity.sqrMagnitude < .36f) return;
            if (owner.MaterialContact(wood, collision.GetContact(0).point, velocity.magnitude))
                AcceptedContactCount++;
        }
    }
}
