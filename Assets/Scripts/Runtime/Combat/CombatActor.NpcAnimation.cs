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
        private Transform[] npcAnimationBaseBones;
        private Vector3[] npcAnimationBasePositions, npcAnimationBaseScales;
        private Quaternion[] npcAnimationBaseRotations;
        internal int NpcAnimationGraphBuilds { get; private set; }

        internal void InitializeNpcAnimation()
        {
            if (npc == null || npc.Animator == null || npcAnimationGraph.IsValid()) return;
            CacheZombieOriginalAnatomy();
            npcAnimationBaseBones = IsZombie ? npc.Animator.GetComponentsInChildren<Transform>(true) : Array.Empty<Transform>();
            int baseCount = npcAnimationBaseBones.Length;
            npcAnimationBasePositions = new Vector3[baseCount];
            npcAnimationBaseScales = new Vector3[baseCount];
            npcAnimationBaseRotations = new Quaternion[baseCount];
            for (int i = 0; i < baseCount; i++)
            {
                npcAnimationBaseBones[i].GetLocalPositionAndRotation(out npcAnimationBasePositions[i], out npcAnimationBaseRotations[i]);
                npcAnimationBaseScales[i] = npcAnimationBaseBones[i].localScale;
            }
            // The resident hands this same Animator to combat. A manual graph
            // retains native clip bindings without adding an animation clock.
            npc.ReleaseAnimation();
            npcAnimationGraph = PlayableGraph.Create("Combat Opponent Body");
            NpcAnimationGraphBuilds++;
            npcAnimationGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            npcAnimationOutput = AnimationPlayableOutput.Create(npcAnimationGraph, "Body", npc.Animator);
            foreach (AnimationClip clip in new[] { ready, zombieWalk, zombieAttackRight, zombieAttackLeft,
                zombieLoadAdjust, zombieWatch, zombieDormant, zombieReveal, zombieFrenzy, hit, defeat })
                NpcAnimationClip(clip);
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
            // The normal entry presents ready first. Keep a direct generic sample
            // correct too, without deriving anatomical offsets from a rise pose.
            if (IsZombie && !zombieAnatomyCaptured && clip != ready) SampleNpcClip(ready, 0f);
            if (!npcAnimationGraph.IsPlaying()) npcAnimationGraph.Play();
            AnimationClipPlayable playable = NpcAnimationClip(clip);
            if (npcSampledClip != clip)
            {
                // Generic import can omit bind-constant tracks. The folded
                // mimic changes those same channels, so switching to an older
                // standing take must not retain a folded limb's translation.
                // Restore only at a clip boundary, before the new take owns it.
                for (int i = 0; i < npcAnimationBaseBones.Length; i++)
                {
                    npcAnimationBaseBones[i].SetLocalPositionAndRotation(npcAnimationBasePositions[i], npcAnimationBaseRotations[i]);
                    npcAnimationBaseBones[i].localScale = npcAnimationBaseScales[i];
                }
                npcAnimationOutput.SetSourcePlayable(playable);
                npcSampledClip = clip;
            }
            bool genericZombie = IsZombie && !IsAuthoredZombieClip(clip);
            if (genericZombie)
            {
                // Generic import may omit bind-constant translations. Rebuild
                // the base before each sample so the anatomical offset below
                // cannot accumulate when the same take is evaluated again.
                for (int i = 0; i < zombieAnatomyBones.Length; i++)
                    zombieAnatomyBones[i].localPosition = zombieOriginalPositions[i];
            }
            playable.SetTime(absoluteTime);
            npcAnimationGraph.Evaluate(0f);
            if (IsZombie)
            {
                if (clip == ready) CaptureZombieAuthoredAnatomy();
                else if (genericZombie) ApplyZombieGenericAnatomy();
            }
        }

        internal void SuspendNpcAnimation()
        {
            // A pooled opponent has already paid for clip bindings at creation.
            // Manual sampling cannot advance while disabled; retain those bindings
            // for the next arrival and release them at the owning actor's teardown.
            if (npcAnimationGraph.IsValid()) npcAnimationGraph.Stop();
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
