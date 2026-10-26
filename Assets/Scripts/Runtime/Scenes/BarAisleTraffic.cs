using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Serializes the bar's narrow left route without revoking a moving body's lease.</summary>
    [DisallowMultipleComponent]
    public sealed class BarAisleTraffic : MonoBehaviour
    {
        private UnityEngine.Object owner, returningOwner;
        public Transform Room { get; private set; }
        public UnityEngine.Object CurrentOwner { get { Prune(); return owner; } }
        public UnityEngine.Object ReturningOwner { get { Prune(); return returningOwner; } }

        public static BarAisleTraffic Create(Transform parent, Transform room)
        {
            if (parent == null || room == null) throw new ArgumentException("Aisle traffic requires the composed room.");
            var carrier = new GameObject("Bar Left Aisle Traffic");
            carrier.transform.SetParent(parent, false);
            var result = carrier.AddComponent<BarAisleTraffic>();
            result.Room = room;
            return result;
        }

        public bool TryAcquire(UnityEngine.Object applicant, bool returning = false)
        {
            if (applicant == null || !isActiveAndEnabled || GameTimeScaleRuntime.IsPaused ||
                SceneTransitionService.IsTransitioning) return false;
            Prune();
            if (returning) RequestReturn(applicant);
            if (ReferenceEquals(owner, applicant)) return true;
            if (owner != null || returningOwner != null && !ReferenceEquals(returningOwner, applicant)) return false;
            owner = applicant;
            if (ReferenceEquals(returningOwner, applicant)) returningOwner = null;
            return true;
        }

        public void RequestReturn(UnityEngine.Object applicant)
        {
            Prune();
            if (applicant != null && (returningOwner == null || ReferenceEquals(returningOwner, applicant)))
                returningOwner = applicant;
        }

        public bool IsOwner(UnityEngine.Object applicant)
        {
            Prune();
            return applicant != null && ReferenceEquals(owner, applicant);
        }

        public bool HasPriorityWaiter(UnityEngine.Object applicant)
        {
            Prune();
            return returningOwner != null && !ReferenceEquals(returningOwner, applicant);
        }

        public bool Release(UnityEngine.Object applicant)
        {
            Prune();
            bool held = applicant != null && ReferenceEquals(owner, applicant);
            if (held) owner = null;
            if (applicant != null && ReferenceEquals(returningOwner, applicant)) returningOwner = null;
            return held;
        }

        private void Prune()
        {
            if (owner == null) owner = null;
            if (returningOwner == null) returningOwner = null;
        }

        private void OnDestroy() { owner = null; returningOwner = null; }
    }
}
