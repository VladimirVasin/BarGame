using UnityEngine;

namespace BarPromenade
{
    /// <summary>Authored gait overrides consumed by the shared NPC footstep director.</summary>
    [DisallowMultipleComponent]
    public sealed class NpcFootstepProfile : MonoBehaviour
    {
        public float Stride { get; private set; }
        public float MinimumSpeed { get; private set; }
        public float VolumeScale { get; private set; }
        public bool UsesAuthoredContacts { get; private set; }
        private bool contactPending;
        private int contactFrame;

        internal void Configure(float stride, float minimumSpeed, float volumeScale, bool authoredContacts = false)
        {
            Stride = Mathf.Max(.01f, stride);
            MinimumSpeed = Mathf.Max(0f, minimumSpeed);
            VolumeScale = Mathf.Clamp01(volumeScale);
            UsesAuthoredContacts = authoredContacts;
        }

        internal void NotifyFootfall() { contactPending = true; contactFrame = Time.frameCount; }
        internal void ResetContacts() => contactPending = false;
        internal bool ConsumeFootfall()
        {
            bool current = contactPending && Time.frameCount - contactFrame <= 2;
            contactPending = false;
            return current;
        }
    }
}
