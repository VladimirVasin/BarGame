using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Live anatomical support; only central bodies also own the first landing sound.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatRagdollGroundContact : MonoBehaviour
    {
        private CombatRagdoll owner;
        private bool central;
        private readonly List<Collider> supportSurfaces = new List<Collider>(2);

        internal bool HasSupportContact
        {
            get
            {
                for (int i = supportSurfaces.Count - 1; i >= 0; i--)
                    if (!CombatRagdoll.IsStaticSupportSurface(supportSurfaces[i])) supportSurfaces.RemoveAt(i);
                return supportSurfaces.Count > 0;
            }
        }

        internal void Initialize(CombatRagdoll target, bool isCentral)
        { owner = target; central = isCentral; ClearSupport(); }
        internal void ClearSupport() => supportSurfaces.Clear();
        private void OnCollisionEnter(Collision collision) => Report(collision);
        private void OnCollisionStay(Collision collision) => Report(collision);
        private void OnCollisionExit(Collision collision)
        { if (collision != null) supportSurfaces.Remove(collision.collider); }
        private void OnDisable() => ClearSupport();
        private void Report(Collision collision)
        {
            if (owner == null || collision == null) return;
            Collider surface = collision.collider;
            if (owner.IsAnatomicalSupport(collision))
            {
                if (!supportSurfaces.Contains(surface)) supportSurfaces.Add(surface);
            }
            else supportSurfaces.Remove(surface);
            if (central) owner.RegisterGroundContact(collision);
        }
    }
}
