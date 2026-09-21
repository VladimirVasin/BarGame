using UnityEngine;

namespace BarPromenade
{
    /// <summary>Combat-only relay from a central ragdoll body to its round owner.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatRagdollGroundContact : MonoBehaviour
    {
        private CombatRagdoll owner;

        internal void Initialize(CombatRagdoll target) => owner = target;
        private void OnCollisionEnter(Collision collision) => Report(collision);
        private void OnCollisionStay(Collision collision) => Report(collision);
        private void Report(Collision collision)
        {
            if (owner != null) owner.RegisterGroundContact(collision);
        }
    }
}
