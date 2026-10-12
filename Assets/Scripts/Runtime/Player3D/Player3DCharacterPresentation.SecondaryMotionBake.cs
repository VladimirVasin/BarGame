using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private void PublishSecondaryMotionClock()
        {
            if (registry == null) return;
            if (ragdollPoseActive || !graph.IsValid())
            { SecondaryMotionBakeRuntime.SetContext(registry, null, 0d); return; }
            if (activeClipBinding != null && activeClipPlayable.IsValid() && !scopedClipLocomotion)
            {
                SecondaryMotionBakeRuntime.SetContext(registry, activeClipBinding.Clip, activeClipPlayable.GetTime(), validateRootMotion: true);
                return;
            }
            // During a mixer transition the physical fallback owns continuity.
            // A single recording is used only after its animation has full weight.
            // Final anatomy and gear guards decide compatibility per part.
            // Carry layers may change arms without changing the head motion.
            for (int i = 0; i <= GaitCount; i++)
            {
                if (locomotionMixer.GetInputWeight(i) < .999f) continue;
                Playable input = locomotionMixer.GetInput(i);
                if (!input.IsValid() || input.GetPlayableType() != typeof(AnimationClipPlayable)) continue;
                AnimationClipPlayable animation = (AnimationClipPlayable)input;
                SecondaryMotionBakeRuntime.SetContext(registry, animation.GetAnimationClip(), animation.GetTime(), validateRootMotion: true);
                return;
            }
            SecondaryMotionBakeRuntime.SetContext(registry, null, 0d);
        }
    }
}
