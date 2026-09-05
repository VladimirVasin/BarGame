using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Drives the cemetery mourner's pose: MournerWalk while the
    /// controller moves her along her route, and one playback of the
    /// MournerMourn rite (lay, sob, wipe) while she stands at the
    /// grave — through a small manual PlayableGraph exactly like the
    /// drying-yard babushkas. The body ships empty-handed; Initialize
    /// attaches the funeral bouquet hand prop to her right grip, and
    /// the controller releases it at the lay cue and places the same
    /// prop prefab on the grave. The presentation stays passive: the
    /// mourner controller owns spawning, movement and despawn.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryMournerPresentation : MonoBehaviour
    {
        /// <summary>A hitch longer than this advances the loop by a
        /// bounded step instead of teleporting mid-stride.</summary>
        public const float MaximumStepSeconds = 0.1f;

        private CityPedestrianHandPropRegistry heldBouquet;

        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        private AnimationPlayableOutput output;
        private bool hasGraph;

        public bool IsInitialized { get; private set; }
        public CityPedestrianAssetRegistry Registry { get; private set; }
        public AnimationClip ActiveClip { get; private set; }

        /// <summary>The bouquet in her hands: attached by
        /// <see cref="Initialize"/>, null after
        /// <see cref="ReleaseHeldBouquet"/>.</summary>
        public CityPedestrianHandPropRegistry HeldBouquet => heldBouquet;

        public void Initialize(
            CityPedestrianAssetRegistry registry,
            int paletteVariant)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (registry.Animator == null)
            {
                throw new InvalidOperationException(
                    "The mourner prefab has no Animator.");
            }

            // Walk slot carries the grieving gait, idle slot the whole
            // graveside rite — the same mapping the art build declares.
            if (registry.WalkClip == null || registry.IdleClip == null)
            {
                throw new InvalidOperationException(
                    "The mourner prefab is missing its authored loops.");
            }

            Registry = registry;
            registry.ApplyPaletteVariant(paletteVariant);

            // After the palette so the bouquet copies the visit's
            // variant; replaced rather than stacked on a re-Initialize.
            CityPedestrianHandProps.Detach(ref heldBouquet);
            heldBouquet = CityPedestrianHandProps.Attach(
                registry,
                CityPedestrianHandPropId.FuneralBouquet,
                paletteVariant);

            graph = PlayableGraph.Create("Cemetery Mourner");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            output = AnimationPlayableOutput.Create(
                graph,
                "Cemetery Mourner Pose",
                registry.Animator);
            graph.Play();
            hasGraph = true;
            Play(registry.WalkClip);
            graph.Evaluate(0f);
            IsInitialized = true;
        }

        public void PlayWalk()
        {
            Play(Registry.WalkClip);
        }

        /// <summary>Starts the single graveside playback: lay the
        /// flowers, thirty seconds of sobbing, wipe the tears.</summary>
        public void PlayMournRite()
        {
            Play(Registry.IdleClip);
        }

        /// <summary>The bouquet has left her hands: the attached prop is
        /// destroyed, and from here on she walks and grieves
        /// empty-handed. Safe to call twice.</summary>
        public void ReleaseHeldBouquet()
        {
            CityPedestrianHandProps.Detach(ref heldBouquet);
        }

        private void Play(AnimationClip clip)
        {
            if (!hasGraph || clip == null || ActiveClip == clip)
            {
                return;
            }

            if (playable.IsValid())
            {
                playable.Destroy();
            }

            playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetTime(0.0);
            output.SetSourcePlayable(playable);
            ActiveClip = clip;
        }

        private void LateUpdate()
        {
            if (!hasGraph)
            {
                return;
            }

            graph.Evaluate(
                Mathf.Min(Time.deltaTime, MaximumStepSeconds));
        }

        private void OnDestroy()
        {
            if (hasGraph && graph.IsValid())
            {
                graph.Destroy();
            }

            hasGraph = false;
            ReleaseHeldBouquet();
        }
    }
}
