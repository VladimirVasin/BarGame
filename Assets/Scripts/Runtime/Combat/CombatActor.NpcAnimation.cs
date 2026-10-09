using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private PlayableGraph npcAnimationGraph;
        private AnimationPlayableOutput npcAnimationOutput;
        private readonly Dictionary<AnimationClip, AnimationClipPlayable> npcAnimationClips =
            new Dictionary<AnimationClip, AnimationClipPlayable>(32);
        private AnimationClip npcSampledClip;

        internal void InitializeNpcAnimation()
        {
            if (npc == null || npc.Animator == null || npcAnimationGraph.IsValid()) return;
            // The resident hands this same Animator to combat. A manual graph
            // retains native clip bindings without adding an animation clock.
            npc.ReleaseAnimation();
            npcAnimationGraph = PlayableGraph.Create("Combat Opponent Body");
            npcAnimationGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            npcAnimationOutput = AnimationPlayableOutput.Create(npcAnimationGraph, "Body", npc.Animator);
            Prepare(CombatAssetProvider.ClipNames);
            Prepare(CombatAssetProvider.StepClipNames);
            Prepare(CombatAssetProvider.LocomotionClipNames);
            Prepare(CombatAssetProvider.RecoveryClipNames);
            npcAnimationGraph.Play();

            void Prepare(IReadOnlyList<string> names)
            {
                foreach (string name in names)
                    if (!CombatAssetProvider.IsKickClip(name)) NpcAnimationClip(CombatAssetProvider.LoadClip(name, true));
            }
        }

        private AnimationClipPlayable NpcAnimationClip(AnimationClip clip)
        {
            if (npcAnimationClips.TryGetValue(clip, out AnimationClipPlayable playable)) return playable;
            playable = AnimationClipPlayable.Create(npcAnimationGraph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetSpeed(0d);
            npcAnimationClips.Add(clip, playable);
            return playable;
        }

        internal void SampleNpcClip(AnimationClip clip, float absoluteTime)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            InitializeNpcAnimation();
            if (!npcAnimationGraph.IsValid()) return;
            AnimationClipPlayable playable = NpcAnimationClip(clip);
            if (npcSampledClip != clip)
            {
                npcAnimationOutput.SetSourcePlayable(playable);
                npcSampledClip = clip;
            }
            playable.SetTime(absoluteTime);
            npcAnimationGraph.Evaluate(0f);
        }

        internal void ReleaseNpcAnimation()
        {
            if (npcAnimationGraph.IsValid()) npcAnimationGraph.Destroy();
            npcAnimationOutput = default;
            npcAnimationClips.Clear();
            npcSampledClip = null;
        }
    }
}
