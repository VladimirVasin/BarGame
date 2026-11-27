using UnityEngine;

namespace BarPromenade
{
    // Release the previous overlay before ordinary Update, IK and attention sample this frame.
    [DefaultExecutionOrder(-31000)]
    internal sealed class NpcGlarePoseRestore : MonoBehaviour
    {
        internal NpcGlareReaction Reaction;
        private void Update() => Reaction?.RestorePose();
    }
}
