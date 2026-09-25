using UnityEngine;

namespace BarPromenade
{
    // Sample once after the player/NPC presentation owners, not during temporary
    // collision previews. No pose or gameplay clock is advanced by this observer.
    [DefaultExecutionOrder(32000)]
    internal sealed class CombatJournalFrameObserver : MonoBehaviour
    {
        internal CombatTestRoot Root;
        private void LateUpdate() { if (Root != null && Root.isActiveAndEnabled) Root.CaptureJournalFrame(); }
    }
}
